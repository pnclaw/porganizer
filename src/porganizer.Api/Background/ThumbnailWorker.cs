using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.Library;
using porganizer.Api.Features.Library.VideoUserImageUpload;
using porganizer.Database;

namespace porganizer.Api.Background;

public class ThumbnailWorker(
    IServiceScopeFactory scopeFactory,
    ThumbnailQueueService queue,
    VideoUserImageUploadQueueService uploadQueue,
    ILogger<ThumbnailWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("ThumbnailWorker started");

        try { await Task.Delay(StartupDelay, ct); }
        catch (OperationCanceledException) { return; }

        await EnqueuePendingFilesAsync(ct);

        await foreach (var fileId in queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IThumbnailGenerationService>();
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
                logger.LogError(ex, "ThumbnailWorker: unhandled error processing file {FileId}", fileId);
            }
        }

        logger.LogInformation("ThumbnailWorker stopped");
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

            // Sprite sheets only exist for matched files; both must be complete before upload
            if (file?.VideoId is not null && file.PreviewImagesGeneratedAtUtc is not null && file.SpriteSheetGeneratedAtUtc is not null)
                uploadQueue.Enqueue(fileId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ThumbnailWorker: failed to check upload eligibility for {FileId}", fileId);
        }
    }

    private async Task EnqueuePendingFilesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Sprite sheets are always matched-only
            var pending = await db.LibraryFiles
                .Where(f => f.SpriteSheetGeneratedAtUtc == null && f.VideoId != null)
                .Select(f => f.Id)
                .ToListAsync(ct);

            if (pending.Count > 0)
            {
                queue.EnqueueMany(pending);
                logger.LogInformation("ThumbnailWorker: enqueued {Count} files for sprite sheet generation", pending.Count);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ThumbnailWorker: error loading pending files on startup");
        }
    }
}
