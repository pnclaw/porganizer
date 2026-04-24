using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.Library;
using porganizer.Database;

namespace porganizer.Api.Background;

public class LibraryIndexQueueWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LibraryIndexQueueWorker> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReindexCooldown = TimeSpan.FromSeconds(45);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("LibraryIndexQueueWorker started");

        try { await Task.Delay(InitialDelay, ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DrainQueueAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LibraryIndexQueueWorker encountered an error");
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("LibraryIndexQueueWorker stopped");
    }

    private async Task DrainQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var requests = await db.LibraryIndexRequests
                .Include(r => r.Folder)
                .OrderBy(r => r.RequestedAtUtc)
                .ToListAsync(ct);

            if (requests.Count == 0)
                return;

            LibraryIndexRequest? nextRequest = null;
            var removedSatisfiedRequest = false;
            foreach (var request in requests)
            {
                var folder = request.Folder;

                if (folder.LastIndexedAtUtc.HasValue &&
                    folder.LastIndexedAtUtc.Value >= request.RequestedAtUtc)
                {
                    db.LibraryIndexRequests.Remove(request);
                    await db.SaveChangesAsync(ct);
                    removedSatisfiedRequest = true;
                    break;
                }

                if (folder.IndexingStartedAtUtc.HasValue)
                    continue;

                if (folder.LastIndexedAtUtc.HasValue)
                {
                    var elapsed = DateTime.UtcNow - folder.LastIndexedAtUtc.Value;
                    if (elapsed < ReindexCooldown)
                        continue;
                }

                nextRequest = request;
                break;
            }

            if (nextRequest is null)
            {
                if (removedSatisfiedRequest)
                    continue;

                return;
            }

            var targetFolder = nextRequest.Folder;
            var folderId = targetFolder.Id;
            var startedAtUtc = DateTime.UtcNow;

            try
            {
                var indexingService = scope.ServiceProvider.GetRequiredService<LibraryIndexingService>();
                await indexingService.IndexFolderAsync(folderId, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "LibraryIndexQueueWorker: queued index failed for folder {FolderId}",
                    folderId);
                return;
            }

            using var cleanupScope = scopeFactory.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var currentRequest = await cleanupDb.LibraryIndexRequests.FindAsync([folderId], ct);
            if (currentRequest is null)
                continue;

            if (currentRequest.RequestedAtUtc <= startedAtUtc)
            {
                cleanupDb.LibraryIndexRequests.Remove(currentRequest);
                await cleanupDb.SaveChangesAsync(ct);
            }
        }
    }
}
