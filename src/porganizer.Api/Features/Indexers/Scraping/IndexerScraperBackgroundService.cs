namespace porganizer.Api.Features.Indexers.Scraping;

public class IndexerScraperBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<IndexerScraperBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Indexer scraper started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunScrapeAsync(stoppingToken);

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public async Task RunScrapeAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var scraper = scope.ServiceProvider.GetRequiredService<IndexerScrapeService>();
        try
        {
            await scraper.ScrapeAllEnabledAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unhandled error during scheduled indexer scrape");
        }
    }
}
