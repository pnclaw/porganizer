using porganizer.Api.Features.Indexers.Matching;
using porganizer.Api.Features.Prdb.Sync;
using porganizer.Api.Features.WantedFulfillment;

namespace porganizer.Api.Background;

public class QuickSyncWorker(IServiceScopeFactory scopeFactory, ILogger<QuickSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("QuickSyncWorker started");

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
                logger.LogError(ex, "QuickSyncWorker encountered an error");
            }

            await Task.Delay(Interval, ct).ConfigureAwait(false);
        }

        logger.LogInformation("QuickSyncWorker stopped");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation("QuickSyncWorker run started at {Time}", DateTimeOffset.UtcNow);

        await RunServiceAsync<PrdbVideoFilehashSyncService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<PrdbIndexerFilehashSyncService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<PrdbVideoUserImageSyncService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<PrdbWantedVideoSyncService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<FavoritesWantedVideoSyncService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<AutoWantedVideoSyncService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<WantedVideoFulfillmentService>(s => s.RunAsync(ct), ct);
        await RunServiceAsync<PrdbDownloadedFromIndexerSyncService>(s => s.RunAsync(ct), ct);

        logger.LogInformation("QuickSyncWorker run completed at {Time}", DateTimeOffset.UtcNow);
    }

    private async Task RunServiceAsync<T>(Func<T, Task> run, CancellationToken ct) where T : notnull
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<T>();
        await run(service);
    }
}
