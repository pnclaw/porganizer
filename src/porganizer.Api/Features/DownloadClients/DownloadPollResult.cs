using porganizer.Database.Enums;

namespace porganizer.Api.Features.DownloadClients;

public class DownloadPollResult
{
    public string ClientItemId { get; init; } = string.Empty;
    public DownloadStatus Status { get; init; }
    public long? TotalSizeBytes { get; init; }
    public long? DownloadedBytes { get; init; }
    public string? StoragePath { get; init; }
    public string? ErrorMessage { get; init; }
}
