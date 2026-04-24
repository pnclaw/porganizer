namespace porganizer.Api.Features.DownloadLogs;

public class MoveResponse
{
    public DownloadLogResponse Log { get; set; } = null!;
    public List<MoveLogEntryResponse> Entries { get; set; } = [];
}

public class MoveLogEntryResponse
{
    /// <summary>0 = Info, 1 = Warning, 2 = Error</summary>
    public int Level { get; set; }
    public string Message { get; set; } = string.Empty;
}
