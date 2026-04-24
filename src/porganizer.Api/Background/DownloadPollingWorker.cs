using porganizer.Api.Features.DownloadClients;

namespace porganizer.Api.Background;

public class DownloadPollingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DownloadPollingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("DownloadPollingWorker started");

        try { await Task.Delay(InitialDelay, ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var pollService = scope.ServiceProvider.GetRequiredService<DownloadPollService>();
                await pollService.PollAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DownloadPollingWorker encountered an error");
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("DownloadPollingWorker stopped");
    }
}
