using porganizer.Api.Features.Library.VideoUserImageUpload;

namespace porganizer.Api.Background;

public class VideoUserImageUploadWorker(
    IServiceScopeFactory scopeFactory,
    VideoUserImageUploadQueueService queue,
    ILogger<VideoUserImageUploadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("VideoUserImageUploadWorker started");

        await foreach (var fileId in queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IVideoUserImageUploadService>();
                await service.UploadAsync(fileId, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "VideoUserImageUploadWorker: unhandled error processing file {FileId}", fileId);
            }
        }

        logger.LogInformation("VideoUserImageUploadWorker stopped");
    }
}
