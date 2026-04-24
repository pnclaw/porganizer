using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using porganizer.Database;
using porganizer.Database.Enums;
using porganizer.Api.Common.Prdb;

namespace porganizer.Api.Features.Library;

public interface IThumbnailGenerationService
{
    Task<bool> GenerateAsync(Guid fileId, CancellationToken ct);
}

public class ThumbnailGenerationService(
    AppDbContext db,
    IOptions<ThumbnailOptions> thumbnailOptions,
    IPrdbUserImageCheckService prdbUserImageCheck,
    ILogger<ThumbnailGenerationService> logger) : IThumbnailGenerationService
{
    private const int TileWidth = 320;
    private const int TileHeight = 180;
    private const int GridCols = 10;
    private const int GridRows = 10;
    private const int MaxTiles = GridCols * GridRows;

    public async Task<bool> GenerateAsync(Guid fileId, CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        if (settings is null || !settings.ThumbnailGenerationEnabled)
        {
            logger.LogDebug("ThumbnailGenerationService: skipping {FileId} — ThumbnailGenerationEnabled is false", fileId);
            return false;
        }

        var file = await db.LibraryFiles
            .Include(f => f.Folder)
            .FirstOrDefaultAsync(f => f.Id == fileId, ct);

        if (file is null)
        {
            logger.LogWarning("ThumbnailGenerationService: LibraryFile {FileId} not found", fileId);
            return false;
        }

        if (file.VideoId is null)
        {
            logger.LogDebug("ThumbnailGenerationService: skipping {FileId} — sprite sheets are for matched files only", fileId);
            return false;
        }

        var fullVideoPath = Path.Combine(file.Folder.Path, file.RelativePath);
        if (!File.Exists(fullVideoPath))
        {
            logger.LogWarning("ThumbnailGenerationService: video file not found at '{Path}'", fullVideoPath);
            return false;
        }

        if (file.VideoId is not null && !string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            var userImageCount = await prdbUserImageCheck.GetUserImageCountAsync(file.VideoId.Value, settings, ct);
            if (userImageCount > 0)
            {
                file.VideoUserImageUploadCompletedAtUtc = DateTime.UtcNow;
                file.VideoUserImageUploadCompletionReason = VideoUserImageUploadCompletionReason.SkippedPrdbAlreadyHasImages;
                file.VideoUserImageUploadRemoteImageCount = userImageCount;
                await db.SaveChangesAsync(ct);

                logger.LogInformation(
                    "ThumbnailGenerationService: skipping {FileId} — video {VideoId} already has {Count} user image(s) on prdb.net",
                    fileId, file.VideoId.Value, userImageCount);
                return false;
            }
        }

        double? duration = await ProbeDurationAsync(settings.FfmpegPath, fullVideoPath, ct);
        if (duration is null or <= 0)
        {
            logger.LogWarning("ThumbnailGenerationService: could not determine duration for '{Path}'", fullVideoPath);
            return false;
        }

        var (interval, tileCount) = CalculateLayout(duration.Value);

        var outputDir = Path.Combine(thumbnailOptions.Value.CachePath, fileId.ToString());
        Directory.CreateDirectory(outputDir);

        var spriteJpg = Path.Combine(outputDir, "sprite.jpg");
        var spriteVtt = Path.Combine(outputDir, "sprite.vtt");

        var success = await RunFfmpegAsync(settings.FfmpegPath, fullVideoPath, spriteJpg, interval, ct);
        if (!success) return false;

        WriteVtt(spriteVtt, tileCount, interval);

        file.SpriteSheetGeneratedAtUtc = DateTime.UtcNow;
        file.SpriteSheetTileCount = tileCount;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "ThumbnailGenerationService: generated sprite sheet for '{Path}' — {TileCount} tiles, interval {Interval:F1}s",
            file.RelativePath, tileCount, interval);

        return true;
    }

    private async Task<double?> ProbeDurationAsync(string ffmpegPath, string videoPath, CancellationToken ct)
    {
        // Derive ffprobe path alongside ffmpeg
        var ffprobePath = DeriveFFprobePath(ffmpegPath);

        var args = $"-v quiet -print_format json -show_entries format=duration \"{videoPath}\"";

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0) return null;

            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.TryGetProperty("format", out var format) &&
                format.TryGetProperty("duration", out var durationEl))
            {
                if (durationEl.ValueKind == JsonValueKind.String &&
                    double.TryParse(durationEl.GetString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var d))
                    return d;

                if (durationEl.ValueKind == JsonValueKind.Number)
                    return durationEl.GetDouble();
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ThumbnailGenerationService: ffprobe failed for '{Path}'", videoPath);
            return null;
        }
    }

    private static string DeriveFFprobePath(string ffmpegPath)
    {
        // If ffmpegPath is just "ffmpeg" or "ffmpeg.exe", use "ffprobe" / "ffprobe.exe"
        var dir = Path.GetDirectoryName(ffmpegPath);
        var ext = Path.GetExtension(ffmpegPath);
        var probeName = string.IsNullOrEmpty(ext) ? "ffprobe" : $"ffprobe{ext}";
        return string.IsNullOrEmpty(dir) ? probeName : Path.Combine(dir, probeName);
    }

    private static (double interval, int tileCount) CalculateLayout(double duration)
    {
        if (duration >= MaxTiles)
        {
            // Long video: spread 100 tiles evenly
            return (duration / MaxTiles, MaxTiles);
        }
        else
        {
            // Short video: 1 frame per second, capped at MaxTiles
            var tiles = Math.Max(1, (int)Math.Floor(duration));
            tiles = Math.Min(tiles, MaxTiles);
            return (1.0, tiles);
        }
    }

    private async Task<bool> RunFfmpegAsync(
        string ffmpegPath, string videoPath, string outputJpg, double interval, CancellationToken ct)
    {
        // fps=1/{interval} extracts one frame every {interval} seconds
        // scale=160:90 resizes each frame
        // tile=10x10 composites up to 100 frames into a sprite sheet grid
        var vf = $"fps=1/{interval.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},scale={TileWidth}:{TileHeight},tile={GridCols}x{GridRows}";
        var args = $"-y -i \"{videoPath}\" -vf \"{vf}\" -frames:v 1 -q:v 5 \"{outputJpg}\"";

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            process.Start();
            // Drain stderr to avoid deadlock; we only log on failure
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stderrText = await stderr;

            if (process.ExitCode != 0)
            {
                logger.LogWarning(
                    "ThumbnailGenerationService: ffmpeg exited with code {Code} for '{Path}': {Stderr}",
                    process.ExitCode, videoPath, stderrText);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ThumbnailGenerationService: ffmpeg failed for '{Path}'", videoPath);
            return false;
        }
    }

    private static void WriteVtt(string vttPath, int tileCount, double interval)
    {
        using var writer = new StreamWriter(vttPath, append: false);
        writer.WriteLine("WEBVTT");
        writer.WriteLine();

        for (var i = 0; i < tileCount; i++)
        {
            var start = i * interval;
            var end = (i + 1) * interval;
            var col = i % GridCols;
            var row = i / GridCols;
            var x = col * TileWidth;
            var y = row * TileHeight;

            writer.WriteLine(FormatVttTimestamp(start) + " --> " + FormatVttTimestamp(end));
            writer.WriteLine($"sprite.jpg#xywh={x},{y},{TileWidth},{TileHeight}");
            writer.WriteLine();
        }
    }

    private static string FormatVttTimestamp(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
