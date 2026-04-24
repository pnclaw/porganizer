using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using porganizer.Database;

namespace porganizer.Api.Features.Library;

public interface IPreviewImageGenerationService
{
    Task<bool> GenerateAsync(Guid fileId, CancellationToken ct);
}

public class PreviewImageGenerationService(
    AppDbContext db,
    IOptions<PreviewOptions> previewOptions,
    ILogger<PreviewImageGenerationService> logger) : IPreviewImageGenerationService
{
    private const int PreviewCount = 5;
    // Positions as fractions of total duration (avoids black frames at start/end)
    private static readonly double[] PositionFractions = [0.10, 0.25, 0.50, 0.75, 0.90];
    // Max output width; height scaled proportionally, rounded to even
    private const int MaxWidth = 1920;
    // JPEG quality: 2 = near-lossless on ffmpeg's 1–31 scale (lower = better)
    private const int JpegQuality = 2;

    public async Task<bool> GenerateAsync(Guid fileId, CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        if (settings is null || !settings.PreviewImageGenerationEnabled)
        {
            logger.LogDebug("PreviewImageGenerationService: skipping {FileId} — PreviewImageGenerationEnabled is false", fileId);
            return false;
        }

        var file = await db.LibraryFiles
            .Include(f => f.Folder)
            .FirstOrDefaultAsync(f => f.Id == fileId, ct);

        if (file is null)
        {
            logger.LogWarning("PreviewImageGenerationService: LibraryFile {FileId} not found", fileId);
            return false;
        }

        if (settings.PreviewImageGenerationMatchedOnly && file.VideoId is null)
        {
            logger.LogDebug("PreviewImageGenerationService: skipping {FileId} — no matched video and MatchedOnly is true", fileId);
            return false;
        }

        var fullVideoPath = Path.Combine(file.Folder.Path, file.RelativePath);
        if (!File.Exists(fullVideoPath))
        {
            logger.LogWarning("PreviewImageGenerationService: video file not found at '{Path}'", fullVideoPath);
            return false;
        }

        double? duration = await ProbeDurationAsync(settings.FfmpegPath, fullVideoPath, ct);
        if (duration is null or <= 0)
        {
            logger.LogWarning("PreviewImageGenerationService: could not determine duration for '{Path}'", fullVideoPath);
            return false;
        }

        var outputDir = Path.Combine(previewOptions.Value.CachePath, fileId.ToString());
        Directory.CreateDirectory(outputDir);

        var tasks = Enumerable.Range(0, PreviewCount)
            .Select(i =>
            {
                var seekSeconds = duration.Value * PositionFractions[i];
                var outputPath = Path.Combine(outputDir, $"preview_{i + 1}.jpg");
                return ExtractFrameAsync(settings.FfmpegPath, fullVideoPath, seekSeconds, outputPath, ct);
            });

        var results = await Task.WhenAll(tasks);
        var generated = results.Count(r => r);

        if (generated == 0)
        {
            logger.LogWarning("PreviewImageGenerationService: all frame extractions failed for '{Path}'", fullVideoPath);
            return false;
        }

        file.PreviewImagesGeneratedAtUtc = DateTime.UtcNow;
        file.PreviewImageCount = generated;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PreviewImageGenerationService: generated {Count}/{Total} preview images for '{Path}'",
            generated, PreviewCount, file.RelativePath);

        return true;
    }

    private async Task<double?> ProbeDurationAsync(string ffmpegPath, string videoPath, CancellationToken ct)
    {
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
            logger.LogWarning(ex, "PreviewImageGenerationService: ffprobe failed for '{Path}'", videoPath);
            return null;
        }
    }

    private async Task<bool> ExtractFrameAsync(
        string ffmpegPath, string videoPath, double seekSeconds, string outputPath, CancellationToken ct)
    {
        // Input-side seek (-ss before -i) for fast seeking.
        // scale='min(MaxWidth,iw):-2' scales down to at most MaxWidth wide, preserving aspect ratio.
        // -2 rounds height to the nearest even number as required by some codecs.
        var seek = seekSeconds.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        var vf = $"scale='min({MaxWidth},iw):-2'";
        var args = $"-y -ss {seek} -i \"{videoPath}\" -frames:v 1 -vf \"{vf}\" -q:v {JpegQuality} \"{outputPath}\"";

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
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stderrText = await stderr;

            if (process.ExitCode != 0)
            {
                logger.LogWarning(
                    "PreviewImageGenerationService: ffmpeg exited with code {Code} for seek {Seek}s in '{Path}': {Stderr}",
                    process.ExitCode, seekSeconds, videoPath, stderrText);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PreviewImageGenerationService: ffmpeg failed for seek {Seek}s in '{Path}'", seekSeconds, videoPath);
            return false;
        }
    }

    private static string DeriveFFprobePath(string ffmpegPath)
    {
        var dir = Path.GetDirectoryName(ffmpegPath);
        var ext = Path.GetExtension(ffmpegPath);
        var probeName = string.IsNullOrEmpty(ext) ? "ffprobe" : $"ffprobe{ext}";
        return string.IsNullOrEmpty(dir) ? probeName : Path.Combine(dir, probeName);
    }
}
