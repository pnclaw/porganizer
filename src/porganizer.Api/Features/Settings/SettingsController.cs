using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using porganizer.Api.Features.DownloadClients;
using porganizer.Api.Features.Library;
using porganizer.Database;
using Serilog.Core;
using Serilog.Events;

namespace porganizer.Api.Features.Settings;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SettingsController(
    AppDbContext db,
    IOptions<ThumbnailOptions> thumbnailOptions,
    IOptions<PreviewOptions> previewOptions,
    LoggingLevelSwitch levelSwitch,
    DownloadLibraryFolderService downloadLibraryFolderService,
    ILogger<SettingsController> logger) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("Get application settings")]
    [EndpointDescription("Returns the current application settings.")]
    [ProducesResponseType(typeof(SettingsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get()
    {
        var settings = await db.GetSettingsAsync();
        return Ok(ToResponse(settings));
    }

    [HttpPut]
    [Consumes("application/json")]
    [EndpointSummary("Update application settings")]
    [EndpointDescription("Updates the application settings.")]
    [ProducesResponseType(typeof(SettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] UpdateSettingsRequest request)
    {
        var settings = await db.GetSettingsAsync();

        settings.PrdbApiKey = request.PrdbApiKey;
        settings.PrdbApiUrl = request.PrdbApiUrl;
        settings.PreferredVideoQuality = request.PreferredVideoQuality;
        settings.SafeForWork = request.SafeForWork;
        settings.DeleteNonVideoFilesOnCompletion = request.DeleteNonVideoFilesOnCompletion;
        settings.CompletedDownloadsTargetFolder = request.CompletedDownloadsTargetFolder;
        settings.OrganizeCompletedBySite = request.OrganizeCompletedBySite;
        settings.RenameCompletedFiles = request.RenameCompletedFiles;
        settings.FavoritesWantedEnabled = request.FavoritesWantedEnabled;
        settings.FavoritesWantedDaysBack = request.FavoritesWantedDaysBack;
        settings.AutoAddAllNewVideos = request.AutoAddAllNewVideos;
        settings.AutoAddAllNewVideosDaysBack = Math.Min(request.AutoAddAllNewVideosDaysBack, 14);
        settings.AutoAddAllNewVideosFulfillAllQualities = request.AutoAddAllNewVideosFulfillAllQualities;
        settings.FfmpegPath = string.IsNullOrWhiteSpace(request.FfmpegPath) ? "ffmpeg" : request.FfmpegPath;
        settings.ThumbnailGenerationEnabled = request.ThumbnailGenerationEnabled;
        settings.ThumbnailGenerationMatchedOnly = request.ThumbnailGenerationMatchedOnly;
        settings.PreviewImageGenerationEnabled = request.PreviewImageGenerationEnabled;
        settings.PreviewImageGenerationMatchedOnly = request.PreviewImageGenerationMatchedOnly;
        settings.VideoUserImageUploadEnabled = request.VideoUserImageUploadEnabled;
        settings.AutoDeleteAfterPreviewUpload = request.AutoDeleteAfterPreviewUpload;
        settings.MinimumLogLevel = request.MinimumLogLevel;
        settings.DownloadLibraryPath = string.IsNullOrWhiteSpace(request.DownloadLibraryPath)
            ? null
            : request.DownloadLibraryPath;

        await db.SaveChangesAsync();

        if (Enum.TryParse<LogEventLevel>(settings.MinimumLogLevel, out var level))
            levelSwitch.MinimumLevel = level;

        await downloadLibraryFolderService.SyncAsync();

        return Ok(ToResponse(settings));
    }

    [HttpPost("reset-prdb-data")]
    [EndpointSummary("Reset database to a clean state")]
    [EndpointDescription("Deletes all synced and operational data, resets all sync cursors, and resets indexer backfill state. Download client settings, indexer settings, library folder paths, folder mappings, and AppSettings configuration are preserved.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPrdbData()
    {
        // Delete children before parents to satisfy FK constraints.
        // Cascade relationships are respected by ordering — explicit deletes are safer
        // than relying on DB-level CASCADE with bulk ExecuteDeleteAsync.

        // Library
        await db.VideoUserImageUploads.ExecuteDeleteAsync();
        await db.LibraryFiles.ExecuteDeleteAsync();
        await db.LibraryIndexRequests.ExecuteDeleteAsync();

        // Indexer operational data
        await db.IndexerApiRequests.ExecuteDeleteAsync();
        await db.IndexerRowMatches.ExecuteDeleteAsync();

        // Downloads
        await db.DownloadLogFiles.ExecuteDeleteAsync();
        await db.DownloadLogs.ExecuteDeleteAsync();
        await db.IndexerRows.ExecuteDeleteAsync();

        // PRDB data — children before parents
        await db.PrdbWantedVideos.ExecuteDeleteAsync();
        await db.PrdbVideoActors.ExecuteDeleteAsync();
        await db.PrdbVideoImages.ExecuteDeleteAsync();
        await db.PrdbVideoFilehashes.ExecuteDeleteAsync();
        await db.PrdbPreDbEntries.ExecuteDeleteAsync();
        await db.PrdbVideos.ExecuteDeleteAsync();
        await db.PrdbSites.ExecuteDeleteAsync();
        await db.PrdbNetworks.ExecuteDeleteAsync();
        await db.PrdbActorImages.ExecuteDeleteAsync();
        await db.PrdbActorAliases.ExecuteDeleteAsync();
        await db.PrdbActors.ExecuteDeleteAsync();

        // Reset all sync cursors and run-at timestamps in AppSettings
        var settings = await db.GetSettingsAsync();
        settings.PrdbActorSyncPage              = 1;
        settings.PrdbActorLastSyncedAt          = null;
        settings.PrdbActorTotalCount            = null;
        settings.SyncWorkerLastRunAt            = null;
        settings.PrdbWantedVideoLastSyncedAt    = null;
        settings.PrdbWantedVideoSyncCursorUtc   = null;
        settings.PrdbWantedVideoSyncCursorId    = null;
        settings.PrdbFavoriteSiteSyncCursorUtc  = null;
        settings.PrdbFavoriteSiteSyncCursorId   = null;
        settings.PrdbFavoriteActorSyncCursorUtc = null;
        settings.PrdbFavoriteActorSyncCursorId  = null;
        settings.IndexerRowMatchLastRunAt       = null;
        settings.PrenamesBackfillPage           = 1;
        settings.PrenamesBackfillTotalCount     = null;
        settings.PrenamesSyncCursorUtc          = null;
        settings.PrdbFilehashBackfillPage       = 1;
        settings.PrdbFilehashBackfillTotalCount = null;
        settings.PrdbFilehashSyncCursorUtc      = null;
        settings.PrdbFilehashSyncCursorId       = null;
        settings.FavoritesWantedLastRunAt       = null;
        settings.AutoAddAllNewVideosLastRunAt   = null;

        // Reset indexer backfill state
        await db.Indexers.ExecuteUpdateAsync(setters => setters
            .SetProperty(i => i.BackfillStartedAtUtc, (DateTime?)null)
            .SetProperty(i => i.BackfillCutoffUtc, (DateTime?)null)
            .SetProperty(i => i.BackfillCompletedAtUtc, (DateTime?)null)
            .SetProperty(i => i.BackfillLastRunAtUtc, (DateTime?)null)
            .SetProperty(i => i.BackfillCurrentOffset, (int?)null));

        await db.SaveChangesAsync();

        // Delete thumbnail and preview image files from disk.
        DeleteCacheDirectories(thumbnailOptions.Value.CachePath, "thumbnail");
        DeleteCacheDirectories(previewOptions.Value.CachePath, "preview");

        return NoContent();
    }

    private void DeleteCacheDirectories(string cachePath, string kind)
    {
        if (!Directory.Exists(cachePath)) return;

        foreach (var dir in Directory.GetDirectories(cachePath))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ResetPrdbData: could not delete {Kind} cache directory '{Dir}'", kind, dir);
            }
        }
    }

    private static SettingsResponse ToResponse(AppSettings settings) => new()
    {
        PrdbApiKey = settings.PrdbApiKey,
        PrdbApiUrl = settings.PrdbApiUrl,
        PreferredVideoQuality = (int)settings.PreferredVideoQuality,
        SafeForWork = settings.SafeForWork,
        DeleteNonVideoFilesOnCompletion = settings.DeleteNonVideoFilesOnCompletion,
        CompletedDownloadsTargetFolder = settings.CompletedDownloadsTargetFolder,
        OrganizeCompletedBySite = settings.OrganizeCompletedBySite,
        RenameCompletedFiles = settings.RenameCompletedFiles,
        FavoritesWantedEnabled = settings.FavoritesWantedEnabled,
        FavoritesWantedDaysBack = settings.FavoritesWantedDaysBack,
        AutoAddAllNewVideos = settings.AutoAddAllNewVideos,
        AutoAddAllNewVideosDaysBack = settings.AutoAddAllNewVideosDaysBack,
        AutoAddAllNewVideosFulfillAllQualities = settings.AutoAddAllNewVideosFulfillAllQualities,
        FfmpegPath = settings.FfmpegPath,
        ThumbnailGenerationEnabled = settings.ThumbnailGenerationEnabled,
        ThumbnailGenerationMatchedOnly = settings.ThumbnailGenerationMatchedOnly,
        PreviewImageGenerationEnabled = settings.PreviewImageGenerationEnabled,
        PreviewImageGenerationMatchedOnly = settings.PreviewImageGenerationMatchedOnly,
        VideoUserImageUploadEnabled = settings.VideoUserImageUploadEnabled,
        AutoDeleteAfterPreviewUpload = settings.AutoDeleteAfterPreviewUpload,
        MinimumLogLevel = settings.MinimumLogLevel,
        DownloadLibraryPath = settings.DownloadLibraryPath,
    };
}
