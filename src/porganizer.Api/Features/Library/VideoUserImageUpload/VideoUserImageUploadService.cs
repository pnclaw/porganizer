using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using porganizer.Database;
using porganizer.Database.Enums;
using porganizer.Api.Common.Prdb;

namespace porganizer.Api.Features.Library.VideoUserImageUpload;

public interface IVideoUserImageUploadService
{
    Task UploadAsync(Guid libraryFileId, CancellationToken ct);
}

public class VideoUserImageUploadService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IPrdbUserImageCheckService prdbUserImageCheck,
    IOptions<PreviewOptions> previewOptions,
    IOptions<ThumbnailOptions> thumbnailOptions,
    ILogger<VideoUserImageUploadService> logger) : IVideoUserImageUploadService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task UploadAsync(Guid libraryFileId, CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        if (settings is null || !settings.VideoUserImageUploadEnabled)
        {
            logger.LogDebug("VideoUserImageUploadService: upload disabled, skipping {FileId}", libraryFileId);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("VideoUserImageUploadService: PrdbApiKey not configured, skipping {FileId}", libraryFileId);
            return;
        }

        var file = await db.LibraryFiles
            .Include(f => f.Folder)
            .FirstOrDefaultAsync(f => f.Id == libraryFileId, ct);
        if (file is null)
        {
            logger.LogWarning("VideoUserImageUploadService: LibraryFile {FileId} not found", libraryFileId);
            return;
        }

        if (string.IsNullOrEmpty(file.OsHash))
        {
            logger.LogDebug("VideoUserImageUploadService: skipping {FileId} — OsHash not computed", libraryFileId);
            return;
        }

        var isMatched = file.VideoId is not null;

        if (file.PreviewImagesGeneratedAtUtc is null || (isMatched && file.SpriteSheetGeneratedAtUtc is null))
        {
            logger.LogDebug("VideoUserImageUploadService: skipping {FileId} — generation not yet complete", libraryFileId);
            return;
        }

        if (file.VideoUserImageUploadCompletedAtUtc is not null)
        {
            logger.LogDebug("VideoUserImageUploadService: skipping {FileId} — already completed", libraryFileId);
            return;
        }

        var localUploadCounts = await db.VideoUserImageUploads
            .Where(u => u.LibraryFileId == libraryFileId)
            .GroupBy(u => u.PreviewImageType)
            .Select(g => new UploadCount(g.Key, g.Count()))
            .ToListAsync(ct);

        if (HasCompleteLocalUpload(file, localUploadCounts))
        {
            MarkUploadCompleted(file, VideoUserImageUploadCompletionReason.Uploaded, localUploadCounts.Sum(c => c.Count));
            await db.SaveChangesAsync(ct);

            logger.LogDebug("VideoUserImageUploadService: skipping {FileId} — already uploaded", libraryFileId);
            return;
        }

        // Check prdb.net for existing images before uploading
        int? existingImageCount;
        if (isMatched)
            existingImageCount = await prdbUserImageCheck.GetUserImageCountAsync(file.VideoId!.Value, settings, ct);
        else
            existingImageCount = await prdbUserImageCheck.GetUserImageCountByOsHashAsync(file.OsHash, settings, ct);

        if (existingImageCount is null)
            return;

        if (existingImageCount > 0)
        {
            MarkUploadCompleted(file, VideoUserImageUploadCompletionReason.SkippedPrdbAlreadyHasImages, existingImageCount.Value);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "VideoUserImageUploadService: skipping {FileId} — already has {Count} user image(s) on prdb.net",
                libraryFileId, existingImageCount.Value);
            return;
        }

        var http = CreateClient(settings);
        var uploads = new List<porganizer.Database.VideoUserImageUpload>();
        var now = DateTime.UtcNow;

        // Upload 5 single-frame previews
        var previewDir = Path.Combine(previewOptions.Value.CachePath, libraryFileId.ToString());
        for (var i = 1; i <= 5; i++)
        {
            ct.ThrowIfCancellationRequested();

            var path = Path.Combine(previewDir, $"preview_{i}.jpg");
            if (!File.Exists(path))
            {
                logger.LogWarning("VideoUserImageUploadService: preview file not found: {Path}", path);
                continue;
            }

            var imageId = await UploadImageAsync(http, file, path, "Single", i - 1, null, ct);
            if (imageId is null) continue;

            uploads.Add(new porganizer.Database.VideoUserImageUpload
            {
                Id = Guid.NewGuid(),
                LibraryFileId = libraryFileId,
                PrdbVideoId = file.VideoId,
                PrdbVideoUserImageId = imageId.Value,
                PreviewImageType = "Single",
                DisplayOrder = i - 1,
                UploadedAtUtc = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        // Upload sprite sheet for matched files only
        if (isMatched)
        {
            var spritePath = Path.Combine(thumbnailOptions.Value.CachePath, libraryFileId.ToString(), "sprite.jpg");
            if (File.Exists(spritePath))
            {
                ct.ThrowIfCancellationRequested();

                var vttPath = Path.Combine(thumbnailOptions.Value.CachePath, libraryFileId.ToString(), "sprite.vtt");
                var spriteId = await UploadImageAsync(http, file, spritePath, "SpriteSheet", 0, vttPath, ct);
                if (spriteId is not null)
                {
                    uploads.Add(new porganizer.Database.VideoUserImageUpload
                    {
                        Id = Guid.NewGuid(),
                        LibraryFileId = libraryFileId,
                        PrdbVideoId = file.VideoId,
                        PrdbVideoUserImageId = spriteId.Value,
                        PreviewImageType = "SpriteSheet",
                        DisplayOrder = 0,
                        UploadedAtUtc = now,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                }
            }
            else
            {
                logger.LogWarning("VideoUserImageUploadService: sprite sheet not found: {Path}", spritePath);
            }
        }

        if (uploads.Count == 0)
        {
            logger.LogWarning("VideoUserImageUploadService: no images uploaded for {FileId}", libraryFileId);
            return;
        }

        db.VideoUserImageUploads.AddRange(uploads);
        await db.SaveChangesAsync(ct);

        var singleCount = uploads.Count(u => u.PreviewImageType == "Single");
        var spriteCount = uploads.Count(u => u.PreviewImageType == "SpriteSheet");
        var allUploaded = singleCount == file.PreviewImageCount && (!isMatched || spriteCount == 1);

        if (allUploaded)
        {
            MarkUploadCompleted(file, VideoUserImageUploadCompletionReason.Uploaded, uploads.Count);
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "VideoUserImageUploadService: uploaded {Count} image(s) for file {FileId} / video {VideoId}",
            uploads.Count, libraryFileId, file.VideoId?.ToString() ?? "unmatched");

        if (settings.AutoDeleteAfterPreviewUpload)
        {
            if (allUploaded)
                DeleteLocalFiles(file, libraryFileId);
            else
                logger.LogWarning(
                    "VideoUserImageUploadService: skipping auto-delete for {FileId} — only {Singles}/{Expected} preview(s) and {Sprites}/1 sprite(s) uploaded",
                    libraryFileId, singleCount, file.PreviewImageCount, spriteCount);
        }
    }

    private static void MarkUploadCompleted(
        LibraryFile file,
        VideoUserImageUploadCompletionReason reason,
        int imageCount)
    {
        file.VideoUserImageUploadCompletedAtUtc = DateTime.UtcNow;
        file.VideoUserImageUploadCompletionReason = reason;
        file.VideoUserImageUploadRemoteImageCount = imageCount;
    }

    private static bool HasCompleteLocalUpload(LibraryFile file, IEnumerable<UploadCount> uploadCounts)
    {
        var singleCount = 0;
        var spriteCount = 0;

        foreach (var uploadCount in uploadCounts)
        {
            if (uploadCount.Type == "Single")
                singleCount = uploadCount.Count;
            else if (uploadCount.Type == "SpriteSheet")
                spriteCount = uploadCount.Count;
        }

        if (file.PreviewImageCount is null) return false;
        if (singleCount != file.PreviewImageCount) return false;
        if (file.VideoId is not null && spriteCount != 1) return false;
        return true;
    }

    private void DeleteLocalFiles(LibraryFile file, Guid libraryFileId)
    {
        var videoPath = Path.Combine(file.Folder.Path, file.RelativePath);
        if (File.Exists(videoPath))
        {
            try
            {
                File.Delete(videoPath);
                logger.LogInformation("VideoUserImageUploadService: deleted video file {Path}", videoPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "VideoUserImageUploadService: failed to delete video file {Path}", videoPath);
            }
        }

        var previewDir = Path.Combine(previewOptions.Value.CachePath, libraryFileId.ToString());
        if (Directory.Exists(previewDir))
        {
            try
            {
                Directory.Delete(previewDir, recursive: true);
                logger.LogInformation("VideoUserImageUploadService: deleted preview directory {Path}", previewDir);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "VideoUserImageUploadService: failed to delete preview directory {Path}", previewDir);
            }
        }

        var thumbnailDir = Path.Combine(thumbnailOptions.Value.CachePath, libraryFileId.ToString());
        if (Directory.Exists(thumbnailDir))
        {
            try
            {
                Directory.Delete(thumbnailDir, recursive: true);
                logger.LogInformation("VideoUserImageUploadService: deleted thumbnail directory {Path}", thumbnailDir);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "VideoUserImageUploadService: failed to delete thumbnail directory {Path}", thumbnailDir);
            }
        }
    }

    private async Task<Guid?> UploadImageAsync(
        HttpClient http,
        LibraryFile file,
        string imagePath,
        string previewImageType,
        int displayOrder,
        string? vttPath,
        CancellationToken ct)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var imageStream = File.OpenRead(imagePath);
                await using var vttStream = vttPath is not null ? File.OpenRead(vttPath) : null;

                using var content = new MultipartFormDataContent();
                content.Add(new StreamContent(imageStream), "File", Path.GetFileName(imagePath));
                if (vttStream is not null)
                    content.Add(new StreamContent(vttStream), "VttFile", "sprite.vtt");
                if (file.VideoId is not null)
                    content.Add(new StringContent(file.VideoId.Value.ToString()), "VideoId");
                content.Add(new StringContent(file.OsHash!), "BasedOnFileWithOsHash");
                content.Add(new StringContent(previewImageType), "PreviewImageType");
                content.Add(new StringContent(displayOrder.ToString()), "DisplayOrder");

                var response = await http.PostAsync("video-user-images", content, ct);

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    logger.LogWarning(
                        "VideoUserImageUploadService: duplicate submission for file {FileId} {Type} order {Order}",
                        file.Id, previewImageType, displayOrder);
                    return null;
                }

                if (IsTransientStatus(response.StatusCode) && attempt < maxAttempts)
                {
                    var delay = GetRetryDelay(response, attempt);
                    logger.LogWarning(
                        "VideoUserImageUploadService: transient {Status} for {Type} order {Order} (attempt {Attempt}/{Max}), retrying in {Seconds}s",
                        (int)response.StatusCode, previewImageType, displayOrder, attempt, maxAttempts, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<SubmitVideoUserImageResponse>(JsonOptions, ct);
                return result?.VideoUserImageId;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt < maxAttempts)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    logger.LogWarning(ex,
                        "VideoUserImageUploadService: network error for {Type} order {Order} (attempt {Attempt}/{Max}), retrying in {Seconds}s",
                        previewImageType, displayOrder, attempt, maxAttempts, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                    continue;
                }

                logger.LogError(ex,
                    "VideoUserImageUploadService: failed to upload {Type} order {Order} for file {FileId}",
                    previewImageType, displayOrder, file.Id);
                return null;
            }
        }

        return null;
    }

    private static bool IsTransientStatus(HttpStatusCode code) =>
        code is HttpStatusCode.ServiceUnavailable
             or HttpStatusCode.BadGateway
             or HttpStatusCode.GatewayTimeout
             or HttpStatusCode.TooManyRequests;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
                return delta;
            if (retryAfter.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) return wait;
            }
        }

        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    private HttpClient CreateClient(AppSettings settings)
    {
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);
        return http;
    }

    private sealed class SubmitVideoUserImageResponse
    {
        public Guid VideoUserImageId { get; set; }
    }

    private sealed record UploadCount(string Type, int Count);
}
