namespace porganizer.Api.Features.DownloadLogs;

public class DownloadLogFileResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? OsHash { get; set; }
}
