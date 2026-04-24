using System.ComponentModel.DataAnnotations;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.Settings;

public class UpdateSettingsRequest
{
    [MaxLength(255)]
    public string PrdbApiKey { get; set; } = string.Empty;

    [MaxLength(255)]
    public string PrdbApiUrl { get; set; } = string.Empty;

    public VideoQuality PreferredVideoQuality { get; set; }

    public bool SafeForWork { get; set; }

    public bool DeleteNonVideoFilesOnCompletion { get; set; }

    [MaxLength(2000)]
    public string? CompletedDownloadsTargetFolder { get; set; }

    public bool OrganizeCompletedBySite { get; set; }

    public bool RenameCompletedFiles { get; set; }

    public bool FavoritesWantedEnabled { get; set; }

    [Range(1, 365)]
    public int FavoritesWantedDaysBack { get; set; } = 7;

    public bool AutoAddAllNewVideos { get; set; }

    [Range(1, 14)]
    public int AutoAddAllNewVideosDaysBack { get; set; } = 2;

    public bool AutoAddAllNewVideosFulfillAllQualities { get; set; }

    [MaxLength(2000)]
    public string FfmpegPath { get; set; } = "ffmpeg";

    public bool ThumbnailGenerationEnabled { get; set; }

    public bool ThumbnailGenerationMatchedOnly { get; set; }

    public bool PreviewImageGenerationEnabled { get; set; }

    public bool PreviewImageGenerationMatchedOnly { get; set; }

    public bool VideoUserImageUploadEnabled { get; set; }

    public bool AutoDeleteAfterPreviewUpload { get; set; }

    [AllowedValues("Verbose", "Debug", "Information", "Warning", "Error", "Fatal")]
    public string MinimumLogLevel { get; set; } = "Information";

    [MaxLength(2000)]
    public string? DownloadLibraryPath { get; set; }

}
