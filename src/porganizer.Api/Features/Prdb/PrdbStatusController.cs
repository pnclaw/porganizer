using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;
using porganizer.Database.Enums;
using porganizer.Api.Features.Indexers.Matching;
using porganizer.Api.Features.Indexers.Scraping;
using porganizer.Api.Features.Prdb.Sync;
using porganizer.Api.Features.WantedFulfillment;

namespace porganizer.Api.Features.Prdb;

[ApiController]
[Route("api/prdb-status")]
[Produces("application/json")]
public class PrdbStatusController(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    PrdbActorSyncService actorSyncService,
    PrdbVideoDetailSyncService videoDetailSyncService,
    PrdbLatestPreDbSyncService latestPreDbSyncService,
    PrdbWantedVideoSyncService wantedVideoSyncService,
    FavoritesWantedVideoSyncService favoritesWantedVideoSyncService,
    PrdbDownloadedFromIndexerSyncService downloadedFromIndexerSyncService,
    PrdbVideoFilehashSyncService filehashSyncService,
    PrdbIndexerFilehashSyncService indexerFilehashSyncService,
    IndexerBackfillService indexerBackfillService,
    IndexerRowMatchService indexerRowMatchService,
    WantedVideoFulfillmentService wantedVideoFulfillmentService) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [HttpGet]
    [EndpointSummary("Get prdb status")]
    [EndpointDescription("Returns actor backfill progress, detail sync progress, library counts, and rate limit info.")]
    [ProducesResponseType(typeof(PrdbStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);

        // ── SyncWorker schedule ───────────────────────────────────────────────
        var syncWorker = new SyncWorkerStatus
        {
            IntervalMinutes = 15,
            LastRunAt       = settings.SyncWorkerLastRunAt,
            NextRunAt       = settings.SyncWorkerLastRunAt?.AddMinutes(15),
        };

        // ── Actor summary backfill ────────────────────────────────────────────
        var actorCount = await db.PrdbActors.CountAsync(ct);

        var actorBackfill = new ActorBackfillStatus
        {
            IsComplete   = settings.PrdbActorSyncPage is null,
            CurrentPage  = settings.PrdbActorSyncPage,
            TotalActors  = settings.PrdbActorTotalCount,
            ActorsInDb   = actorCount,
            LastSyncedAt = settings.PrdbActorLastSyncedAt,
        };

        // ── Actor detail sync ─────────────────────────────────────────────────
        var actorsWithDetail  = await db.PrdbActors.CountAsync(a => a.DetailSyncedAtUtc != null, ct);
        var favoriteActors    = await db.PrdbActors.CountAsync(a => a.IsFavorite, ct);

        var actorDetailSync = new ActorDetailSyncStatus
        {
            ActorsWithDetail  = actorsWithDetail,
            ActorsPending     = actorCount - actorsWithDetail,
            TotalActors       = actorCount,
            FavoriteActors    = favoriteActors,
        };

        // ── Video detail sync ─────────────────────────────────────────────────
        var videoCount       = await db.PrdbVideos.CountAsync(ct);
        var videosWithDetail = await db.PrdbVideos.CountAsync(v => v.DetailSyncedAtUtc != null, ct);
        var videosWithCast   = await db.PrdbVideoActors.Select(va => va.VideoId).Distinct().CountAsync(ct);

        var videoDetailSync = new VideoDetailSyncStatus
        {
            VideosWithDetail = videosWithDetail,
            VideosPending    = videoCount - videosWithDetail,
            TotalVideos      = videoCount,
            VideosWithCast   = videosWithCast,
        };

        // ── Wanted video sync ─────────────────────────────────────────────────
        var totalWanted     = await db.PrdbWantedVideos.CountAsync(ct);
        var fulfilled       = await db.PrdbWantedVideos.CountAsync(w => w.IsFulfilled, ct);
        var pendingDetail   = await db.PrdbWantedVideos
            .Join(db.PrdbVideos, w => w.VideoId, v => v.Id, (w, v) => v.DetailSyncedAtUtc)
            .CountAsync(d => d == null, ct);

        var wantedVideoSync = new WantedVideoSyncStatus
        {
            Total         = totalWanted,
            Unfulfilled   = totalWanted - fulfilled,
            Fulfilled     = fulfilled,
            PendingDetail = pendingDetail,
            LastSyncedAt  = settings.PrdbWantedVideoLastSyncedAt,
        };

        // ── Favorites-wanted sync ─────────────────────────────────────────────
        var favoritesWantedSync = new FavoritesWantedSyncStatus
        {
            IsEnabled    = settings.FavoritesWantedEnabled,
            DaysBack     = settings.FavoritesWantedDaysBack,
            LastRunAt    = settings.FavoritesWantedLastRunAt,
        };

        // ── Downloaded-from-indexer sync ─────────────────────────────────────
        var completedLogs = await db.DownloadLogs
            .Where(l => l.Status == DownloadStatus.Completed)
            .Select(l => new
            {
                l.PrdbDownloadedFromIndexerSyncedAtUtc,
                l.PrdbDownloadedFromIndexerSyncError,
            })
            .ToListAsync(ct);

        var downloadedFromIndexerSync = new DownloadedFromIndexerSyncStatus
        {
            Synced  = completedLogs.Count(l => l.PrdbDownloadedFromIndexerSyncedAtUtc != null && l.PrdbDownloadedFromIndexerSyncError == null),
            Errors  = completedLogs.Count(l => l.PrdbDownloadedFromIndexerSyncError != null),
            Pending = completedLogs.Count(l => l.PrdbDownloadedFromIndexerSyncedAtUtc == null && l.PrdbDownloadedFromIndexerSyncError == null),
        };

        // ── Prename sync ──────────────────────────────────────────────────────
        var totalPreDbEntries = await db.PrdbPreDbEntries.CountAsync(ct);
        var totalLinkedPreDbEntries = await db.PrdbPreDbEntries.CountAsync(p => p.PrdbVideoId != null, ct);

        var preNameSync = new PreNameSyncStatus
        {
            TotalPreNames       = totalLinkedPreDbEntries,
            TotalPreDbEntries   = totalPreDbEntries,
            IsBackfilling       = settings.PrenamesBackfillPage is not null || settings.PrenamesSyncCursorUtc is null,
            BackfillPage        = settings.PrenamesBackfillPage,
            BackfillTotalCount  = settings.PrenamesBackfillTotalCount,
            LastSyncedAt        = settings.PrenamesSyncCursorUtc,
        };

        // ── Filehash sync ─────────────────────────────────────────────────────
        var totalFilehashes    = await db.PrdbVideoFilehashes.CountAsync(ct);
        var verifiedFilehashes = await db.PrdbVideoFilehashes.CountAsync(f => f.IsVerified, ct);

        var filehashSync = new FilehashSyncStatus
        {
            TotalInDb          = totalFilehashes,
            Verified           = verifiedFilehashes,
            IsBackfilling      = settings.PrdbFilehashBackfillPage is not null || settings.PrdbFilehashSyncCursorUtc is null,
            BackfillPage       = settings.PrdbFilehashBackfillPage,
            BackfillTotalCount = settings.PrdbFilehashBackfillTotalCount,
            LastSyncedAt       = settings.PrdbFilehashSyncCursorUtc,
        };

        // ── Indexer filehash sync ─────────────────────────────────────────────
        var totalIndexerFilehashes    = await db.PrdbIndexerFilehashes.CountAsync(ct);
        var verifiedIndexerFilehashes = await db.PrdbIndexerFilehashes.CountAsync(f => f.IsVerified, ct);

        var indexerFilehashSync = new IndexerFilehashSyncStatus
        {
            TotalInDb          = totalIndexerFilehashes,
            Verified           = verifiedIndexerFilehashes,
            IsBackfilling      = settings.PrdbIndexerFilehashBackfillPage is not null || settings.PrdbIndexerFilehashSyncCursorUtc is null,
            BackfillPage       = settings.PrdbIndexerFilehashBackfillPage,
            BackfillTotalCount = settings.PrdbIndexerFilehashBackfillTotalCount,
            LastSyncedAt       = settings.PrdbIndexerFilehashSyncCursorUtc,
        };

        // ── Library counts ────────────────────────────────────────────────────
        var library = new LibraryCounts
        {
            Networks       = await db.PrdbNetworks.CountAsync(ct),
            Sites          = await db.PrdbSites.CountAsync(ct),
            FavoriteSites  = await db.PrdbSites.CountAsync(s => s.IsFavorite, ct),
            Videos         = videoCount,
            PreDbEntries   = totalPreDbEntries,
            PreNames       = totalLinkedPreDbEntries,
            Actors         = actorCount,
            FavoriteActors = favoriteActors,
            ActorImages    = await db.PrdbActorImages.CountAsync(ct),
            VideoImages    = await db.PrdbVideoImages.CountAsync(ct),
            WantedVideos   = totalWanted,
            Filehashes     = totalFilehashes,
        };

        // ── Indexer row match sync ────────────────────────────────────────────
        var totalMatches = await db.IndexerRowMatches.CountAsync(ct);

        var weekAgo = DateTime.UtcNow.AddDays(-7);

        var topGrouped = await db.IndexerRows
            .GroupBy(r => r.IndexerId)
            .Select(g => new
            {
                IndexerId    = g.Key,
                TotalRows    = g.Count(),
                RowsLastWeek = g.Count(r => r.CreatedAt >= weekAgo),
            })
            .OrderByDescending(g => g.TotalRows)
            .Take(3)
            .ToListAsync(ct);

        var topIndexerIds = topGrouped.Select(g => g.IndexerId).ToList();
        var indexerTitles = await db.Indexers
            .Where(i => topIndexerIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Title })
            .ToDictionaryAsync(i => i.Id, i => i.Title, ct);
        var topIndexers = topGrouped
            .Select(g => new IndexerRowStat
            {
                Title        = indexerTitles.GetValueOrDefault(g.IndexerId, "Unknown"),
                TotalRows    = g.TotalRows,
                RowsLastWeek = g.RowsLastWeek,
            })
            .ToList();

        var indexerBackfills = await db.Indexers
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Title)
            .Select(i => new IndexerBackfillStatus
            {
                IndexerId = i.Id,
                IndexerTitle = i.Title,
                IsEnabled = i.IsEnabled,
                Days = i.BackfillDays,
                IsComplete = i.BackfillCompletedAtUtc != null,
                StartedAtUtc = i.BackfillStartedAtUtc,
                CutoffUtc = i.BackfillCutoffUtc,
                CompletedAtUtc = i.BackfillCompletedAtUtc,
                LastRunAtUtc = i.BackfillLastRunAtUtc,
                CurrentOffset = i.BackfillCurrentOffset,
            })
            .ToListAsync(ct);

        var indexerRowMatchSync = new IndexerRowMatchSyncStatus
        {
            TotalMatches = totalMatches,
            LastRunAt    = settings.IndexerRowMatchLastRunAt,
            TopIndexers  = topIndexers,
        };

        // ── Preview image upload ──────────────────────────────────────────────
        var filesUploaded = await db.VideoUserImageUploads
            .Select(u => u.LibraryFileId).Distinct().CountAsync(ct);

        var imagesUploaded = await db.VideoUserImageUploads.CountAsync(ct);

        var filesPending = await db.LibraryFiles
            .Where(f => f.VideoId != null
                     && f.PreviewImagesGeneratedAtUtc != null
                     && f.SpriteSheetGeneratedAtUtc != null
                     && f.VideoUserImageUploadCompletedAtUtc == null
                     && !(f.PreviewImageCount != null
                          && db.VideoUserImageUploads.Count(u => u.LibraryFileId == f.Id && u.PreviewImageType == "SpriteSheet") == 1
                          && db.VideoUserImageUploads.Count(u => u.LibraryFileId == f.Id && u.PreviewImageType == "Single") == f.PreviewImageCount))
            .CountAsync(ct);

        var lastUploadedAt = await db.VideoUserImageUploads
            .OrderByDescending(u => u.UploadedAtUtc)
            .Select(u => (DateTime?)u.UploadedAtUtc)
            .FirstOrDefaultAsync(ct);

        var previewQuery = db.LibraryFiles.Where(f => f.PreviewImagesGeneratedAtUtc == null);
        if (settings.PreviewImageGenerationMatchedOnly)
            previewQuery = previewQuery.Where(f => f.VideoId != null);
        var filesAwaitingPreviewGeneration = await previewQuery.CountAsync(ct);

        var thumbnailQuery = db.LibraryFiles.Where(f => f.SpriteSheetGeneratedAtUtc == null);
        if (settings.ThumbnailGenerationMatchedOnly)
            thumbnailQuery = thumbnailQuery.Where(f => f.VideoId != null);
        var filesAwaitingThumbnailGeneration = await thumbnailQuery.CountAsync(ct);

        var previewImageUpload = new PreviewImageUploadStatus
        {
            IsEnabled                     = settings.VideoUserImageUploadEnabled,
            AutoDeleteEnabled             = settings.AutoDeleteAfterPreviewUpload,
            FilesUploaded                 = filesUploaded,
            ImagesUploaded                = imagesUploaded,
            FilesPending                  = filesPending,
            LastUploadedAt                = lastUploadedAt,
            FilesAwaitingPreviewGeneration    = filesAwaitingPreviewGeneration,
            FilesAwaitingThumbnailGeneration  = filesAwaitingThumbnailGeneration,
        };

        // ── Rate limits ───────────────────────────────────────────────────────
        PrdbRateLimitStatus? rateLimit = null;
        if (!string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            try
            {
                var http = httpClientFactory.CreateClient();
                http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
                http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);
                rateLimit = await http.GetFromJsonAsync<PrdbRateLimitStatus>("rate-limit", JsonOptions, ct);
            }
            catch { /* rate limit unavailable — return null */ }
        }

        return Ok(new PrdbStatusResponse
        {
            SyncWorker                  = syncWorker,
            ActorBackfill               = actorBackfill,
            ActorDetailSync             = actorDetailSync,
            VideoDetailSync             = videoDetailSync,
            PreNameSync                 = preNameSync,
            FilehashSync                = filehashSync,
            IndexerFilehashSync         = indexerFilehashSync,
            WantedVideoSync             = wantedVideoSync,
            FavoritesWantedSync         = favoritesWantedSync,
            DownloadedFromIndexerSync   = downloadedFromIndexerSync,
            IndexerBackfills            = indexerBackfills,
            IndexerRowMatchSync         = indexerRowMatchSync,
            PreviewImageUpload          = previewImageUpload,
            Library                     = library,
            RateLimit                   = rateLimit,
        });
    }

    [HttpPost("backfill/run")]
    [EndpointSummary("Run actor backfill")]
    [EndpointDescription("Manually triggers one actor summary backfill run, identical to the scheduled SyncWorker tick.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RunBackfill(CancellationToken ct)
    {
        await actorSyncService.RunAsync(ct);
        return NoContent();
    }

    [HttpPost("video-detail-sync/run")]
    [EndpointSummary("Run video detail sync")]
    [EndpointDescription("Manually triggers one video detail sync run, identical to the scheduled SyncWorker tick.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RunVideoDetailSync(CancellationToken ct)
    {
        await videoDetailSyncService.RunAsync(ct);
        return NoContent();
    }

    [HttpPost("prename-sync/run")]
    [EndpointSummary("Run PreDb sync")]
    [EndpointDescription("Manually triggers one PreDb sync run, identical to the scheduled SyncWorker tick.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RunPreNameSync(CancellationToken ct)
    {
        await latestPreDbSyncService.RunAsync(ct);
        return NoContent();
    }

    [HttpPost("prename-sync/reset-cursor")]
    [EndpointSummary("Reset PreDb sync cursor")]
    [EndpointDescription("Clears the PreDb sync cursor so the next run performs a full backfill from the beginning.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPreNameCursor(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        settings.PrenamesSyncCursorUtc = null;
        settings.PrenamesBackfillPage  = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("wanted-video-sync/run")]
    [EndpointSummary("Run wanted video sync")]
    [EndpointDescription("Manually triggers one wanted video sync run, identical to the scheduled SyncWorker tick.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RunWantedVideoSync(CancellationToken ct)
    {
        await wantedVideoSyncService.RunAsync(ct);
        return NoContent();
    }

    [HttpPost("wanted-video-sync/reset-cursor")]
    [EndpointSummary("Reset wanted video sync cursor")]
    [EndpointDescription("Clears the wanted-video change cursor so the next run replays the wanted-video change feed from the beginning.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetWantedVideoCursor(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        settings.PrdbWantedVideoSyncCursorUtc = null;
        settings.PrdbWantedVideoSyncCursorId = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("filehash-sync/run")]
    [EndpointSummary("Run filehash sync")]
    [EndpointDescription("Manually triggers one filehash sync run, identical to the scheduled SyncWorker tick.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RunFilehashSync(CancellationToken ct)
    {
        await filehashSyncService.RunAsync(ct);
        return NoContent();
    }

    [HttpPost("filehash-sync/reset-cursor")]
    [EndpointSummary("Reset filehash sync cursor")]
    [EndpointDescription("Clears the filehash sync cursor so the next run performs a full backfill from the beginning.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetFilehashCursor(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        settings.PrdbFilehashSyncCursorUtc  = null;
        settings.PrdbFilehashSyncCursorId   = null;
        settings.PrdbFilehashBackfillPage   = 1;
        settings.PrdbFilehashBackfillTotalCount = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("indexer-filehash-sync/run")]
    [EndpointSummary("Run indexer filehash sync")]
    [EndpointDescription("Manually triggers one indexer filehash sync run, identical to the scheduled SyncWorker tick.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RunIndexerFilehashSync(CancellationToken ct)
    {
        await indexerFilehashSyncService.RunAsync(ct);
        return NoContent();
    }

    [HttpPost("indexer-filehash-sync/reset-cursor")]
    [EndpointSummary("Reset indexer filehash sync cursor")]
    [EndpointDescription("Clears the indexer filehash sync cursor so the next run performs a full backfill from the beginning.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetIndexerFilehashCursor(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        settings.PrdbIndexerFilehashSyncCursorUtc     = null;
        settings.PrdbIndexerFilehashSyncCursorId      = null;
        settings.PrdbIndexerFilehashBackfillPage      = 1;
        settings.PrdbIndexerFilehashBackfillTotalCount = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("downloaded-from-indexer-sync/run")]
    [EndpointSummary("Run downloaded-from-indexer sync")]
    [EndpointDescription("Manually triggers one downloaded-from-indexer sync run, identical to the scheduled SyncWorker tick.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RunDownloadedFromIndexerSync(CancellationToken ct)
    {
        await downloadedFromIndexerSyncService.RunAsync(ct);
        return NoContent();
    }

    [HttpPost("indexer-backfill/{id:guid}/run")]
    [EndpointSummary("Run indexer backfill")]
    [EndpointDescription("Manually triggers one backfill step for a specific enabled indexer.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RunIndexerBackfill(Guid id, CancellationToken ct)
    {
        if (!await db.Indexers.AnyAsync(i => i.Id == id, ct))
            return NotFound();

        await indexerBackfillService.RunIndexerAsync(id, ct);
        return NoContent();
    }

    [HttpPost("wanted-fulfillment/run")]
    [EndpointSummary("Run wanted video fulfillment")]
    [EndpointDescription("Manually triggers one wanted video fulfillment run, identical to the scheduled SyncWorker tick.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RunWantedFulfillment(CancellationToken ct)
    {
        await wantedVideoFulfillmentService.RunAsync(ct);
        return NoContent();
    }

    [HttpPost("indexer-row-match/run")]
    [EndpointSummary("Run indexer row match sync")]
    [EndpointDescription("Manually triggers one indexer row match run, identical to the scheduled SyncWorker tick.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RunIndexerRowMatch(CancellationToken ct)
    {
        await indexerRowMatchService.RunAsync(ct);
        return NoContent();
    }

    [HttpPost("indexer-row-match/debug")]
    [EndpointSummary("Debug indexer row match")]
    [EndpointDescription("Read-only diagnostic run filtered by a search string. Returns match status for every matching row without writing to the database.")]
    [ProducesResponseType(typeof(IndexerRowMatchDebugResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> DebugIndexerRowMatch(
        [FromBody] IndexerRowMatchDebugRequest request, CancellationToken ct)
    {
        var result = await indexerRowMatchService.RunDebugAsync(request.Search, ct);
        return Ok(result);
    }

    [HttpPost("favorites-wanted-sync/run")]
    [EndpointSummary("Run favorites-wanted sync")]
    [EndpointDescription("Manually triggers one favorites-wanted sync run, identical to the scheduled SyncWorker tick.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RunFavoritesWantedSync(CancellationToken ct)
    {
        await favoritesWantedVideoSyncService.RunAsync(ct);
        return NoContent();
    }

    [HttpPost("favorite-site-sync/reset-cursor")]
    [EndpointSummary("Reset favorite site sync cursor")]
    [EndpointDescription("Clears the favorite-site change cursor so the next aggregate PRDB sync replays favorite-site changes from the beginning.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetFavoriteSiteCursor(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        settings.PrdbFavoriteSiteSyncCursorUtc = null;
        settings.PrdbFavoriteSiteSyncCursorId = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("favorite-actor-sync/reset-cursor")]
    [EndpointSummary("Reset favorite actor sync cursor")]
    [EndpointDescription("Clears the favorite-actor change cursor so the next aggregate PRDB sync replays favorite-actor changes from the beginning.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetFavoriteActorCursor(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        settings.PrdbFavoriteActorSyncCursorUtc = null;
        settings.PrdbFavoriteActorSyncCursorId = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public class IndexerRowMatchDebugRequest
{
    public string Search { get; init; } = string.Empty;
}

public class PrdbStatusResponse
{
    public SyncWorkerStatus SyncWorker { get; init; } = null!;
    public ActorBackfillStatus ActorBackfill { get; init; } = null!;
    public ActorDetailSyncStatus ActorDetailSync { get; init; } = null!;
    public VideoDetailSyncStatus VideoDetailSync { get; init; } = null!;
    public PreNameSyncStatus PreNameSync { get; init; } = null!;
    public FilehashSyncStatus FilehashSync { get; init; } = null!;
    public IndexerFilehashSyncStatus IndexerFilehashSync { get; init; } = null!;
    public WantedVideoSyncStatus WantedVideoSync { get; init; } = null!;
    public FavoritesWantedSyncStatus FavoritesWantedSync { get; init; } = null!;
    public DownloadedFromIndexerSyncStatus DownloadedFromIndexerSync { get; init; } = null!;
    public List<IndexerBackfillStatus> IndexerBackfills { get; init; } = [];
    public IndexerRowMatchSyncStatus IndexerRowMatchSync { get; init; } = null!;
    public PreviewImageUploadStatus PreviewImageUpload { get; init; } = null!;
    public LibraryCounts Library { get; init; } = null!;
    public PrdbRateLimitStatus? RateLimit { get; init; }
}

public class SyncWorkerStatus
{
    public int IntervalMinutes { get; init; }
    public DateTime? LastRunAt { get; init; }
    public DateTime? NextRunAt { get; init; }
}

public class ActorBackfillStatus
{
    public bool IsComplete { get; init; }
    public int? CurrentPage { get; init; }
    public int? TotalActors { get; init; }
    public int ActorsInDb { get; init; }
    public DateTime? LastSyncedAt { get; init; }
}

public class ActorDetailSyncStatus
{
    public int ActorsWithDetail { get; init; }
    public int ActorsPending { get; init; }
    public int TotalActors { get; init; }
    public int FavoriteActors { get; init; }
}

public class VideoDetailSyncStatus
{
    public int VideosWithDetail { get; init; }
    public int VideosPending { get; init; }
    public int TotalVideos { get; init; }
    public int VideosWithCast { get; init; }
}

public class DownloadedFromIndexerSyncStatus
{
    public int Synced  { get; init; }
    public int Errors  { get; init; }
    public int Pending { get; init; }
}

public class WantedVideoSyncStatus
{
    public int Total { get; init; }
    public int Unfulfilled { get; init; }
    public int Fulfilled { get; init; }
    public int PendingDetail { get; init; }
    public DateTime? LastSyncedAt { get; init; }
}

public class FavoritesWantedSyncStatus
{
    public bool IsEnabled { get; init; }
    public int DaysBack { get; init; }
    public DateTime? LastRunAt { get; init; }
}

public class IndexerBackfillStatus
{
    public Guid IndexerId { get; init; }
    public string IndexerTitle { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public int Days { get; init; }
    public bool IsComplete { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CutoffUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public DateTime? LastRunAtUtc { get; init; }
    public int? CurrentOffset { get; init; }
}

public class IndexerRowMatchSyncStatus
{
    public int TotalMatches { get; init; }
    public DateTime? LastRunAt { get; init; }
    public List<IndexerRowStat> TopIndexers { get; init; } = [];
}

public class IndexerRowStat
{
    public string Title { get; init; } = string.Empty;
    public int TotalRows { get; init; }
    public int RowsLastWeek { get; init; }
}

public class FilehashSyncStatus
{
    public int TotalInDb { get; init; }
    public int Verified { get; init; }
    public bool IsBackfilling { get; init; }
    public int? BackfillPage { get; init; }
    public int? BackfillTotalCount { get; init; }
    public DateTime? LastSyncedAt { get; init; }
}

public class IndexerFilehashSyncStatus
{
    public int TotalInDb { get; init; }
    public int Verified { get; init; }
    public bool IsBackfilling { get; init; }
    public int? BackfillPage { get; init; }
    public int? BackfillTotalCount { get; init; }
    public DateTime? LastSyncedAt { get; init; }
}

public class PreNameSyncStatus
{
    public int TotalPreNames { get; init; }
    public int TotalPreDbEntries { get; init; }
    public bool IsBackfilling { get; init; }
    public int? BackfillPage { get; init; }
    public int? BackfillTotalCount { get; init; }
    public DateTime? LastSyncedAt { get; init; }
}

public class PreviewImageUploadStatus
{
    public bool IsEnabled { get; init; }
    public bool AutoDeleteEnabled { get; init; }
    public int FilesUploaded { get; init; }
    public int ImagesUploaded { get; init; }
    public int FilesPending { get; init; }
    public DateTime? LastUploadedAt { get; init; }
    public int FilesAwaitingPreviewGeneration { get; init; }
    public int FilesAwaitingThumbnailGeneration { get; init; }
}

public class LibraryCounts
{
    public int Networks { get; init; }
    public int Sites { get; init; }
    public int FavoriteSites { get; init; }
    public int Videos { get; init; }
    public int PreDbEntries { get; init; }
    public int PreNames { get; init; }
    public int Actors { get; init; }
    public int FavoriteActors { get; init; }
    public int ActorImages { get; init; }
    public int VideoImages { get; init; }
    public int WantedVideos { get; init; }
    public int Filehashes { get; init; }
}

public class PrdbRateLimitStatus
{
    public bool IsEnforced { get; init; }
    public RateLimitWindow Hourly { get; init; } = null!;
    public RateLimitWindow Monthly { get; init; } = null!;
}

public class RateLimitWindow
{
    public int Limit { get; init; }
    public int Used { get; init; }
    public int Remaining { get; init; }
    public int ResetsInSeconds { get; init; }
}
