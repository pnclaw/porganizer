using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using porganizer.Api.Features.Library.VideoUserImageUpload;
using porganizer.Database;

namespace porganizer.Api.Features.Library;

[ApiController]
[Route("api")]
public class LibraryThumbnailsController(
    AppDbContext db,
    ThumbnailQueueService queue,
    PreviewQueueService previewQueue,
    VideoUserImageUploadQueueService uploadQueue,
    IOptions<ThumbnailOptions> thumbnailOptions,
    IOptions<PreviewOptions> previewOptions,
    ILogger<LibraryThumbnailsController> logger) : ControllerBase
{
    [HttpGet("library-files/{id:guid}/sprite-sheet")]
    [EndpointSummary("Get sprite sheet for a library file")]
    [EndpointDescription("Returns the sprite sheet JPEG for the given library file. 404 if not yet generated.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSpriteSheet(Guid id, CancellationToken ct)
    {
        var file = await db.LibraryFiles
            .Where(f => f.Id == id && f.SpriteSheetGeneratedAtUtc != null)
            .Select(f => new { f.Id })
            .FirstOrDefaultAsync(ct);

        if (file is null) return NotFound();

        var path = Path.Combine(thumbnailOptions.Value.CachePath, id.ToString(), "sprite.jpg");
        if (!System.IO.File.Exists(path)) return NotFound();

        return PhysicalFile(path, "image/jpeg");
    }

    [HttpGet("library-files/{id:guid}/sprite-vtt")]
    [EndpointSummary("Get sprite VTT for a library file")]
    [EndpointDescription("Returns the WebVTT thumbnail track for the given library file.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSpriteVtt(Guid id, CancellationToken ct)
    {
        var file = await db.LibraryFiles
            .Where(f => f.Id == id && f.SpriteSheetGeneratedAtUtc != null)
            .Select(f => new { f.Id })
            .FirstOrDefaultAsync(ct);

        if (file is null) return NotFound();

        var path = Path.Combine(thumbnailOptions.Value.CachePath, id.ToString(), "sprite.vtt");
        if (!System.IO.File.Exists(path)) return NotFound();

        return PhysicalFile(path, "text/vtt");
    }

    [HttpPost("library-files/{id:guid}/generate-thumbnails")]
    [EndpointSummary("Trigger thumbnail generation for a library file")]
    [EndpointDescription("Enqueues the given library file for sprite sheet generation. Existing sprite sheet will be regenerated.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateForFile(Guid id, CancellationToken ct)
    {
        var exists = await db.LibraryFiles.AnyAsync(f => f.Id == id, ct);
        if (!exists) return NotFound();

        // Clear the existing generation timestamp so the worker regenerates it
        await db.LibraryFiles
            .Where(f => f.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.SpriteSheetGeneratedAtUtc, (DateTime?)null)
                .SetProperty(f => f.SpriteSheetTileCount, (int?)null), ct);

        queue.Enqueue(id);
        return Accepted();
    }

    [HttpPost("library-thumbnails/validate-ffmpeg")]
    [Consumes("application/json")]
    [EndpointSummary("Validate an ffmpeg binary path")]
    [EndpointDescription("Runs 'ffmpeg -version' with the supplied path and returns the version string on success, or an error message on failure.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateFfmpeg(
        [FromBody] ValidateFfmpegRequest request,
        CancellationToken ct)
    {
        var path = string.IsNullOrWhiteSpace(request.FfmpegPath) ? "ffmpeg" : request.FfmpegPath.Trim();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = "-version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var output = await stdout + await stderr;

            if (process.ExitCode != 0)
                return Ok(new { ok = false, message = $"ffmpeg exited with code {process.ExitCode}." });

            // First line of `ffmpeg -version` is e.g. "ffmpeg version 7.1 Copyright ..."
            var version = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
                ?? "ffmpeg responded but version line was empty.";

            return Ok(new { ok = true, message = version });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return Ok(new { ok = false, message = $"Could not start ffmpeg at '{path}': binary not found." });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, message = $"Error running ffmpeg: {ex.Message}" });
        }
    }

    [HttpPost("library-thumbnails/generate-all")]
    [EndpointSummary("Trigger thumbnail generation for all library files")]
    [EndpointDescription("Enqueues all library files that do not yet have a sprite sheet for generation.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> GenerateAll(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        var matchedOnly = settings?.ThumbnailGenerationMatchedOnly ?? false;

        var query = db.LibraryFiles.Where(f => f.SpriteSheetGeneratedAtUtc == null);
        if (matchedOnly)
            query = query.Where(f => f.VideoId != null);

        var pending = await query.Select(f => f.Id).ToListAsync(ct);

        queue.EnqueueMany(pending);
        return Accepted(new { enqueued = pending.Count });
    }

    [HttpPost("library-thumbnails/reset-all")]
    [EndpointSummary("Reset all generated thumbnails")]
    [EndpointDescription("Clears sprite sheet metadata from all library files and deletes the cached sprite files from disk. Thumbnails will need to be regenerated.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetAll(CancellationToken ct)
    {
        var cleared = await db.LibraryFiles
            .Where(f => f.SpriteSheetGeneratedAtUtc != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.SpriteSheetGeneratedAtUtc, (DateTime?)null)
                .SetProperty(f => f.SpriteSheetTileCount, (int?)null), ct);

        var cachePath = thumbnailOptions.Value.CachePath;
        if (Directory.Exists(cachePath))
        {
            foreach (var dir in Directory.GetDirectories(cachePath))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "ResetAll: could not delete cache directory '{Dir}'", dir);
                }
            }
        }

        return Ok(new { cleared });
    }

    [HttpGet("library-files/{id:guid}/previews/{n:int}")]
    [EndpointSummary("Get a preview image for a library file")]
    [EndpointDescription("Returns preview image n (1–5) for the given library file. 404 if not yet generated or n is out of range.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPreviewImage(Guid id, int n, CancellationToken ct)
    {
        if (n < 1 || n > 5) return NotFound();

        var file = await db.LibraryFiles
            .Where(f => f.Id == id && f.PreviewImagesGeneratedAtUtc != null)
            .Select(f => new { f.PreviewImageCount })
            .FirstOrDefaultAsync(ct);

        if (file is null || file.PreviewImageCount < n) return NotFound();

        var path = Path.Combine(previewOptions.Value.CachePath, id.ToString(), $"preview_{n}.jpg");
        if (!System.IO.File.Exists(path)) return NotFound();

        return PhysicalFile(path, "image/jpeg");
    }

    [HttpPost("library-previews/generate-all")]
    [EndpointSummary("Trigger preview image generation for all library files")]
    [EndpointDescription("Enqueues all library files that do not yet have preview images for generation.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> GenerateAllPreviews(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        var matchedOnly = settings?.PreviewImageGenerationMatchedOnly ?? false;

        var query = db.LibraryFiles.Where(f => f.PreviewImagesGeneratedAtUtc == null);
        if (matchedOnly)
            query = query.Where(f => f.VideoId != null);

        var pending = await query.Select(f => f.Id).ToListAsync(ct);
        previewQueue.EnqueueMany(pending);
        return Accepted(new { enqueued = pending.Count });
    }

    [HttpPost("library-previews/reset-all")]
    [EndpointSummary("Reset all generated preview images")]
    [EndpointDescription("Clears preview image metadata from all library files and deletes the cached preview files from disk. Previews will need to be regenerated.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetAllPreviews(CancellationToken ct)
    {
        var cleared = await db.LibraryFiles
            .Where(f => f.PreviewImagesGeneratedAtUtc != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.PreviewImagesGeneratedAtUtc, (DateTime?)null)
                .SetProperty(f => f.PreviewImageCount, (int?)null), ct);

        var cachePath = previewOptions.Value.CachePath;
        if (Directory.Exists(cachePath))
        {
            foreach (var dir in Directory.GetDirectories(cachePath))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "ResetAllPreviews: could not delete cache directory '{Dir}'", dir);
                }
            }
        }

        return Ok(new { cleared });
    }

    [HttpPost("library-previews/upload-all")]
    [EndpointSummary("Enqueue all eligible preview images for upload to prdb.net")]
    [EndpointDescription("Enqueues all library files whose previews and sprite sheet have been generated but have not yet been uploaded to prdb.net.")]
    [ProducesResponseType(typeof(UploadAllResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadAllPreviews(CancellationToken ct)
    {
        var pending = await db.LibraryFiles
            .Where(f =>
                f.PreviewImagesGeneratedAtUtc != null &&
                f.SpriteSheetGeneratedAtUtc != null &&
                f.VideoId != null &&
                f.OsHash != null &&
                f.VideoUserImageUploadCompletedAtUtc == null &&
                !(f.PreviewImageCount != null &&
                  db.VideoUserImageUploads.Count(u => u.LibraryFileId == f.Id && u.PreviewImageType == "SpriteSheet") == 1 &&
                  db.VideoUserImageUploads.Count(u => u.LibraryFileId == f.Id && u.PreviewImageType == "Single") == f.PreviewImageCount))
            .Select(f => f.Id)
            .ToListAsync(ct);

        foreach (var id in pending)
            uploadQueue.Enqueue(id);

        return Ok(new UploadAllResponse { Enqueued = pending.Count });
    }
}

public class UploadAllResponse
{
    public int Enqueued { get; set; }
}

public class ValidateFfmpegRequest
{
    [MaxLength(2000)]
    public string? FfmpegPath { get; set; }
}
