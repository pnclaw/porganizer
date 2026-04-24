using System.ComponentModel.DataAnnotations;
using porganizer.Database.Enums;

namespace porganizer.Database;

public class AppSettings
{
    public int Id { get; set; } = 1;

    [MaxLength(255)]
    public string PrdbApiKey { get; set; } = string.Empty;

    [MaxLength(255)]
    public string PrdbApiUrl { get; set; } = "https://api.prdb.net";

    public VideoQuality PreferredVideoQuality { get; set; } = VideoQuality.P2160;

    /// <summary>
    /// Next page to fetch during actor backfill. Null means the backfill is complete.
    /// </summary>
    public int? PrdbActorSyncPage { get; set; } = 1;

    /// <summary>
    /// Set when the backfill completes. Used as the CreatedAfter cursor for new-actor checks.
    /// </summary>
    public DateTime? PrdbActorLastSyncedAt { get; set; }

    /// <summary>
    /// Total actor count on prdb as of last backfill page. Used for progress display.
    /// </summary>
    public int? PrdbActorTotalCount { get; set; }

    /// <summary>
    /// Set at the end of each successful SyncWorker run. Used to calculate next scheduled run.
    /// </summary>
    public DateTime? SyncWorkerLastRunAt { get; set; }

    /// <summary>
    /// Set at the end of each successful wanted-video sync run. Used for status display.
    /// </summary>
    public DateTime? PrdbWantedVideoLastSyncedAt { get; set; }

    /// <summary>
    /// Used as the UpdatedAtUtc cursor when consuming the wanted-video changes feed.
    /// Null means the wanted-video change backfill has not completed yet.
    /// </summary>
    public DateTime? PrdbWantedVideoSyncCursorUtc { get; set; }

    /// <summary>
    /// Tie-breaker for <see cref="PrdbWantedVideoSyncCursorUtc"/> when consuming the seek-paged wanted-video changes feed.
    /// </summary>
    public Guid? PrdbWantedVideoSyncCursorId { get; set; }

    /// <summary>
    /// Used as the UpdatedAtUtc cursor when consuming the favorite-site changes feed.
    /// Null means favorite-site change sync has not completed an initial pass yet.
    /// </summary>
    public DateTime? PrdbFavoriteSiteSyncCursorUtc { get; set; }

    /// <summary>
    /// Tie-breaker for <see cref="PrdbFavoriteSiteSyncCursorUtc"/> when consuming the seek-paged favorite-site changes feed.
    /// </summary>
    public Guid? PrdbFavoriteSiteSyncCursorId { get; set; }

    /// <summary>
    /// Used as the UpdatedAtUtc cursor when consuming the favorite-actor changes feed.
    /// Null means favorite-actor change sync has not completed an initial pass yet.
    /// </summary>
    public DateTime? PrdbFavoriteActorSyncCursorUtc { get; set; }

    /// <summary>
    /// Tie-breaker for <see cref="PrdbFavoriteActorSyncCursorUtc"/> when consuming the seek-paged favorite-actor changes feed.
    /// </summary>
    public Guid? PrdbFavoriteActorSyncCursorId { get; set; }

    /// <summary>
    /// When true, images from the prdb API are blurred in the UI.
    /// </summary>
    public bool SafeForWork { get; set; }

    /// <summary>
    /// Set at the end of each successful indexer-row match run. Used for status display.
    /// </summary>
    public DateTime? IndexerRowMatchLastRunAt { get; set; }

    /// <summary>
    /// Next page to fetch during prename backfill. Not null means backfill is in progress.
    /// Null means backfill is complete — check PrenamesSyncCursorUtc for incremental state.
    /// </summary>
    public int? PrenamesBackfillPage { get; set; }

    /// <summary>
    /// Total prename count on prdb as of the last backfill page. Used for progress display.
    /// </summary>
    public int? PrenamesBackfillTotalCount { get; set; }

    /// <summary>
    /// Set when backfill completes. Used as CreatedFrom cursor for incremental sync thereafter.
    /// Null means backfill has never completed.
    /// </summary>
    public DateTime? PrenamesSyncCursorUtc { get; set; }

    /// <summary>
    /// When true, non-video files in a completed download folder are deleted from disk automatically.
    /// </summary>
    public bool DeleteNonVideoFilesOnCompletion { get; set; }

    /// <summary>
    /// Root folder where completed downloads are moved. Must exist on disk.
    /// </summary>
    [MaxLength(2000)]
    public string? CompletedDownloadsTargetFolder { get; set; }

    /// <summary>
    /// When true, completed downloads are moved into a subfolder named after the linked prdb.net site.
    /// Requires <see cref="CompletedDownloadsTargetFolder"/> to be set.
    /// </summary>
    public bool OrganizeCompletedBySite { get; set; }

    /// <summary>
    /// When true, video files are renamed to "{SiteName} - {Title} - {ReleaseDate} - {Quality}" on move.
    /// When false, original filenames are kept.
    /// </summary>
    public bool RenameCompletedFiles { get; set; }

    /// <summary>
    /// Next page to fetch during filehash backfill (oldest-first). Not null means backfill is in progress.
    /// Null means backfill is complete — check PrdbFilehashSyncCursorUtc for incremental state.
    /// </summary>
    public int? PrdbFilehashBackfillPage { get; set; } = 1;

    /// <summary>
    /// Total filehash count on prdb as of the last backfill page. Used for progress display.
    /// </summary>
    public int? PrdbFilehashBackfillTotalCount { get; set; }

    /// <summary>
    /// Set when backfill completes. Used as the UpdatedAtUtc cursor for incremental filehash changes.
    /// Null means backfill has never completed.
    /// </summary>
    public DateTime? PrdbFilehashSyncCursorUtc { get; set; }

    /// <summary>
    /// Tie-breaker for <see cref="PrdbFilehashSyncCursorUtc"/> when consuming the seek-paged filehash changes feed.
    /// </summary>
    public Guid? PrdbFilehashSyncCursorId { get; set; }

    /// <summary>
    /// Seek cursor (updatedAtUtc) for the video user images delta feed.
    /// Null means the feed has never been consumed — sync will start from the beginning.
    /// </summary>
    public DateTime? PrdbVideoUserImageSyncCursorUtc { get; set; }

    /// <summary>
    /// Tie-breaker for <see cref="PrdbVideoUserImageSyncCursorUtc"/> when consuming the seek-paged video user image changes feed.
    /// </summary>
    public Guid? PrdbVideoUserImageSyncCursorId { get; set; }

    /// <summary>
    /// When true, videos linked to a favorite site or actor that were added to prdb.net within
    /// <see cref="FavoritesWantedDaysBack"/> days are automatically added to the wanted list each sync.
    /// </summary>
    public bool FavoritesWantedEnabled { get; set; } = false;

    /// <summary>
    /// How many days back to look for new videos when auto-adding favorites to the wanted list.
    /// </summary>
    public int FavoritesWantedDaysBack { get; set; } = 7;

    /// <summary>
    /// Set at the end of each successful favorites-wanted sync run. Used for status display.
    /// </summary>
    public DateTime? FavoritesWantedLastRunAt { get; set; }

    /// <summary>
    /// When true, any video added to prdb.net within <see cref="AutoAddAllNewVideosDaysBack"/> days
    /// that has at least one indexer match is automatically added to the wanted list on every sync.
    /// </summary>
    public bool AutoAddAllNewVideos { get; set; } = false;

    /// <summary>
    /// How many days back to look for new videos when auto-adding all matched videos to the wanted list.
    /// Capped at 14 days.
    /// </summary>
    public int AutoAddAllNewVideosDaysBack { get; set; } = 2;

    /// <summary>
    /// Set at the end of each successful auto-add-all sync run. Used for status display.
    /// </summary>
    public DateTime? AutoAddAllNewVideosLastRunAt { get; set; }

    /// <summary>
    /// When true, videos that are auto-added via <see cref="AutoAddAllNewVideos"/> are queued for
    /// download in all available qualities (720p, 1080p, 2160p), regardless of
    /// <see cref="PreferredVideoQuality"/>. This lets the system collect file hashes for every
    /// quality variant.
    /// </summary>
    public bool AutoAddAllNewVideosFulfillAllQualities { get; set; } = false;

    /// <summary>
    /// Next page to fetch during indexer filehash backfill (oldest-first). Not null means backfill is in progress.
    /// Null means backfill is complete — check PrdbIndexerFilehashSyncCursorUtc for incremental state.
    /// </summary>
    public int? PrdbIndexerFilehashBackfillPage { get; set; } = 1;

    /// <summary>
    /// Total indexer filehash count on prdb as of the last backfill page. Used for progress display.
    /// </summary>
    public int? PrdbIndexerFilehashBackfillTotalCount { get; set; }

    /// <summary>
    /// Set when backfill completes. Used as the UpdatedAtUtc cursor for incremental indexer filehash changes.
    /// Null means backfill has never completed.
    /// </summary>
    public DateTime? PrdbIndexerFilehashSyncCursorUtc { get; set; }

    /// <summary>
    /// Tie-breaker for <see cref="PrdbIndexerFilehashSyncCursorUtc"/> when consuming the seek-paged indexer filehash changes feed.
    /// </summary>
    public Guid? PrdbIndexerFilehashSyncCursorId { get; set; }

    /// <summary>
    /// Path to the ffmpeg binary. Defaults to "ffmpeg" (resolved from PATH).
    /// </summary>
    [MaxLength(2000)]
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>
    /// When true, sprite-sheet thumbnails are generated in the background for all library files.
    /// The output folder is determined automatically from the data directory.
    /// </summary>
    public bool ThumbnailGenerationEnabled { get; set; }

    /// <summary>
    /// When true, thumbnail generation is restricted to library files that have been matched
    /// to a prdb.net video (VideoId is not null). When false, thumbnails are generated for
    /// all library files regardless of match status.
    /// </summary>
    public bool ThumbnailGenerationMatchedOnly { get; set; }

    /// <summary>
    /// When true, high-quality preview images are generated in the background for library files.
    /// 5 frames are extracted at evenly spaced positions, scaled to a max width of 1920px.
    /// </summary>
    public bool PreviewImageGenerationEnabled { get; set; }

    /// <summary>
    /// When true, preview image generation is restricted to library files matched to a prdb.net
    /// video (VideoId is not null). When false, previews are generated for all library files.
    /// </summary>
    public bool PreviewImageGenerationMatchedOnly { get; set; }

    /// <summary>
    /// When true, locally generated preview images (single frames and sprite sheets) are
    /// automatically uploaded to prdb.net after generation, provided the video has no existing
    /// user images on prdb.net yet.
    /// </summary>
    public bool VideoUserImageUploadEnabled { get; set; }

    /// <summary>
    /// When true, the video file and its locally generated preview images and sprite sheet are
    /// deleted from disk after all processing is complete: file hash computed, preview images
    /// generated and uploaded to prdb.net.
    /// </summary>
    public bool AutoDeleteAfterPreviewUpload { get; set; }

    /// <summary>
    /// Minimum log level for the application. Valid values match Serilog's LogEventLevel:
    /// Verbose, Debug, Information, Warning, Error, Fatal.
    /// </summary>
    [MaxLength(20)]
    public string MinimumLogLevel { get; set; } = "Information";

    /// <summary>
    /// Root folder where downloaded files land when they are not moved to a library folder
    /// (either because move is disabled or the download has no prdb.net match).
    /// When set, this folder is automatically registered as a library folder so that completed
    /// downloads are indexed and eligible for preview/thumbnail generation.
    /// </summary>
    [MaxLength(2000)]
    public string? DownloadLibraryPath { get; set; }

}
