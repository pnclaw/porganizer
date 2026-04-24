using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.Library;
using porganizer.Api.Features.Library.VideoUserImageUpload;
using porganizer.Database;

namespace porganizer.Api.Background;

public class PreviewWorker(
    IServiceScopeFactory scopeFactory,
    PreviewQueueService queue,
    VideoUserImageUploadQueueService uploadQueue,
    ILogger<PreviewWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("PreviewWorker started");

        try { await Task.Delay(StartupDelay, ct); }
        catch (OperationCanceledException) { return; }

        await EnqueuePendingFilesAsync(ct);

        await foreach (var fileId in queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IPreviewImageGenerationService>();
                var generated = await service.GenerateAsync(fileId, ct);

                if (generated)
                    await MaybeEnqueueUploadAsync(fileId, scope, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PreviewWorker: unhandled error processing file {FileId}", fileId);
            }
        }

        logger.LogInformation("PreviewWorker stopped");
    }

    private async Task MaybeEnqueueUploadAsync(Guid fileId, IServiceScope scope, CancellationToken ct)
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var file = await db.LibraryFiles
                .AsNoTracking()
                .Where(f => f.Id == fileId)
                .Select(f => new { f.VideoId, f.PreviewImagesGeneratedAtUtc, f.SpriteSheetGeneratedAtUtc })
                .FirstOrDefaultAsync(ct);

            if (file is null) return;

            // Unmatched files upload after previews alone; matched files wait for sprite sheet too
            var readyForUpload = file.VideoId is null
                ? file.PreviewImagesGeneratedAtUtc is not null
                : file.PreviewImagesGeneratedAtUtc is not null && file.SpriteSheetGeneratedAtUtc is not null;

            if (readyForUpload)
                uploadQueue.Enqueue(fileId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "PreviewWorker: failed to check upload eligibility for {FileId}", fileId);
        }
    }

    private async Task EnqueuePendingFilesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var settings = await db.GetSettingsAsync(ct);
            if (settings is null || !settings.PreviewImageGenerationEnabled)
                return;

            var matchedOnly = settings.PreviewImageGenerationMatchedOnly;

            var query = db.LibraryFiles.Where(f => f.PreviewImagesGeneratedAtUtc == null);
            if (matchedOnly)
                query = query.Where(f => f.VideoId != null);

            var pending = await query
                .Select(f => f.Id)
                .ToListAsync(ct);

            if (pending.Count > 0)
            {
                queue.EnqueueMany(pending);
                logger.LogInformation("PreviewWorker: enqueued {Count} files for preview image generation", pending.Count);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PreviewWorker: error loading pending files on startup");
        }
    }
}
