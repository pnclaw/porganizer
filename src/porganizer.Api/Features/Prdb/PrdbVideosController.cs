using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.DownloadLogs;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.Prdb;

[ApiController]
[Route("api/prdb-videos")]
[Produces("application/json")]
public class PrdbVideosController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List videos")]
    [EndpointDescription("Returns a paged list of videos ordered by release date descending. Optionally filter by search or site.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] Guid? siteId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24)
    {
        var q = db.PrdbVideos.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            q = q.Where(v =>
                EF.Functions.Like(v.Title, pattern) ||
                EF.Functions.Like(v.Site.Title, pattern));
        }

        if (siteId.HasValue)
            q = q.Where(v => v.SiteId == siteId.Value);

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(v => v.ReleaseDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new PrdbVideoListResponse
            {
                Id               = v.Id,
                Title            = v.Title,
                ReleaseDate      = v.ReleaseDate,
                SiteId           = v.SiteId,
                SiteTitle        = v.Site.Title,
                ThumbnailCdnPath = v.Images.Select(i => i.CdnPath).FirstOrDefault(),
                ActorCount       = v.VideoActors.Count,
                IsWanted         = db.PrdbWantedVideos.Any(w => w.VideoId == v.Id),
                IsFulfilled      = db.PrdbWantedVideos.Where(w => w.VideoId == v.Id).Select(w => (bool?)w.IsFulfilled).FirstOrDefault(),
                HasIndexerMatch  = db.IndexerRowMatches.Any(m => m.PrdbVideoId == v.Id),
            })
            .ToListAsync();

        return Ok(new { items, total });
    }

    [HttpGet("filter-options")]
    [EndpointSummary("Get filter options for videos")]
    [EndpointDescription("Returns the distinct sites present in the video library, for use in filter dropdowns.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFilterOptions()
    {
        var sites = await db.PrdbSites
            .Where(s => db.PrdbVideos.Any(v => v.SiteId == s.Id))
            .OrderBy(s => s.Title)
            .Select(s => new { s.Id, s.Title })
            .ToListAsync();

        return Ok(new { sites });
    }

    [HttpGet("{id:guid}")]
    [EndpointSummary("Get video detail")]
    [EndpointDescription("Returns full detail for a single video including images, cast, pre-names, and wanted status.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id)
    {
        var video = await db.PrdbVideos
            .Include(v => v.Site)
            .Include(v => v.Images)
            .Include(v => v.PreDbEntries)
            .Include(v => v.VideoActors)
                .ThenInclude(va => va.Actor)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (video is null) return NotFound();

        var wanted = await db.PrdbWantedVideos.FindAsync(id);

        return Ok(new PrdbVideoDetailResponse
        {
            Id            = video.Id,
            Title         = video.Title,
            ReleaseDate   = video.ReleaseDate,
            SiteId        = video.SiteId,
            SiteTitle     = video.Site.Title,
            SiteUrl       = video.Site.Url,
            ImageCdnPaths = video.Images
                .Where(i => i.CdnPath != null)
                .Select(i => i.CdnPath!)
                .ToList(),
            Actors = video.VideoActors
                .Select(va => new PrdbVideoDetailActorResponse
                {
                    Id   = va.Actor.Id,
                    Name = va.Actor.Name,
                })
                .OrderBy(a => a.Name)
                .ToList(),
            PreNames    = video.PreDbEntries.Select(p => p.Title).ToList(),
            IsFulfilled = wanted?.IsFulfilled,
        });
    }

    [HttpGet("{id:guid}/indexer-matches")]
    [EndpointSummary("Get indexer matches for a video")]
    [EndpointDescription("Returns all indexer rows matched to this video, along with their latest download status if any.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIndexerMatches(Guid id)
    {
        if (!await db.PrdbVideos.AnyAsync(v => v.Id == id))
            return NotFound();

        var rows = await db.IndexerRowMatches
            .Where(m => m.PrdbVideoId == id)
            .OrderByDescending(m => m.IndexerRow.NzbPublishedAt)
            .Select(m => new
            {
                m.IndexerRow.Id,
                m.IndexerRow.IndexerId,
                IndexerTitle = m.IndexerRow.Indexer.Title,
                m.IndexerRow.Title,
                m.IndexerRow.NzbUrl,
                m.IndexerRow.NzbSize,
                m.IndexerRow.NzbPublishedAt,
                m.IndexerRow.Category,
            })
            .ToListAsync();

        var rowIds = rows.Select(r => r.Id).ToList();

        var latestLogs = await db.DownloadLogs
            .Where(l => rowIds.Contains(l.IndexerRowId))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new
            {
                l.IndexerRowId,
                l.Status,
                l.StoragePath,
                Files = l.Files.Select(f => new { f.Id, f.FileName, f.OsHash }).ToList(),
            })
            .ToListAsync();

        var logByRow = latestLogs
            .GroupBy(l => l.IndexerRowId)
            .ToDictionary(g => g.Key, g => g.First());

        var matches = rows.Select(r =>
        {
            logByRow.TryGetValue(r.Id, out var log);
            return new VideoIndexerMatchResponse
            {
                IndexerRowId   = r.Id,
                IndexerId      = r.IndexerId,
                IndexerTitle   = r.IndexerTitle,
                Title          = r.Title,
                NzbUrl         = r.NzbUrl,
                NzbSize        = r.NzbSize,
                NzbPublishedAt = r.NzbPublishedAt,
                Category       = r.Category,
                DownloadStatus = log == null ? null : (DownloadStatus?)log.Status,
                StoragePath    = log?.StoragePath,
                Files          = log?.Files.Count > 0
                    ? log.Files.Select(f => new DownloadLogFileResponse { Id = f.Id, FileName = f.FileName, OsHash = f.OsHash }).ToList()
                    : null,
            };
        }).ToList();

        return Ok(matches);
    }
}
