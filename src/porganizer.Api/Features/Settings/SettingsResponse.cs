namespace porganizer.Api.Features.Settings;

public class SettingsResponse
{
    public string PrdbApiKey { get; set; } = string.Empty;
    public string PrdbApiUrl { get; set; } = string.Empty;

    /// <summary>Preferred video quality integer value (0 = 720p, 1 = 1080p, 2 = 2160p).</summary>
    public int PreferredVideoQuality { get; set; }

    public bool SafeForWork { get; set; }

    public bool DeleteNonVideoFilesOnCompletion { get; set; }

    public string? CompletedDownloadsTargetFolder { get; set; }

    public bool OrganizeCompletedBySite { get; set; }

    public bool RenameCompletedFiles { get; set; }

    public bool FavoritesWantedEnabled { get; set; }

    public int FavoritesWantedDaysBack { get; set; }

    public bool AutoAddAllNewVideos { get; set; }

    public int AutoAddAllNewVideosDaysBack { get; set; }

    public bool AutoAddAllNewVideosFulfillAllQualities { get; set; }

    public string FfmpegPath { get; set; } = "ffmpeg";

    public bool ThumbnailGenerationEnabled { get; set; }

    public bool ThumbnailGenerationMatchedOnly { get; set; }

    public bool PreviewImageGenerationEnabled { get; set; }

    public bool PreviewImageGenerationMatchedOnly { get; set; }

    public bool VideoUserImageUploadEnabled { get; set; }

    public bool AutoDeleteAfterPreviewUpload { get; set; }

    public string MinimumLogLevel { get; set; } = "Information";

    public string? DownloadLibraryPath { get; set; }

}
