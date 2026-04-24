using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.Indexers.Matching;
using porganizer.Api.Features.Indexers.Scraping;
using porganizer.Api.Features.Library;
using porganizer.Api.Features.Prdb.Sync;
using porganizer.Database;

namespace porganizer.Api.Background;

public class SyncWorker(IServiceScopeFactory scopeFactory, ILogger<SyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("SyncWorker started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SyncWorker encountered an error");
            }

            await Task.Delay(Interval, ct).ConfigureAwait(false);
        }

        logger.LogInformation("SyncWorker stopped");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation("SyncWorker run started at {Time}", DateTimeOffset.UtcNow);

        await RunServiceAsync<PrdbActorSyncService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<PrdbVideoDetailSyncService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<PrdbLatestPreDbSyncService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<FavoritesWantedVideoSyncService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<IndexerBackfillService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<IndexerRowMatchService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<LibraryIndexingService>(s => s.IndexAllAsync(ct), ct);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.GetSettingsAsync(ct);
        settings.SyncWorkerLastRunAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("SyncWorker run completed at {Time}", DateTimeOffset.UtcNow);
    }

    private async Task RunServiceAsync<T>(Func<T, Task> run, CancellationToken ct) where T : notnull
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<T>();
        await run(service);
    }
}
