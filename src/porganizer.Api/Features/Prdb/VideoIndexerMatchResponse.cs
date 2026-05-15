using porganizer.Api.Features.DownloadLogs;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.Prdb;

public class VideoIndexerMatchResponse
{
    public Guid IndexerRowId { get; set; }
    public Guid IndexerId { get; set; }
    public string IndexerTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string NzbUrl { get; set; } = string.Empty;
    public long NzbSize { get; set; }
    public DateTime? NzbPublishedAt { get; set; }
    public int Category { get; set; }
    public DownloadStatus? DownloadStatus { get; set; }
    public string? StoragePath { get; set; }
    public List<DownloadLogFileResponse>? Files { get; set; }
}
