namespace porganizer.Database.Enums;

public enum DownloadStatus
{
    Queued         = 0,
    Downloading    = 1,
    PostProcessing = 2,
    Completed      = 3,
    Failed         = 4,
}
