using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb;

[ApiController]
[Route("api/prdb-predb")]
[Produces("application/json")]
public class PrdbPreDbController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List latest PreDB entries")]
    [EndpointDescription("Returns a paged list of latest PreDB entries ordered by created timestamp descending. Supports search, site filter, and linked-video filtering.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] Guid? siteId,
        [FromQuery] bool? hasLinkedVideo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = db.PrdbPreDbEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            q = q.Where(p =>
                EF.Functions.Like(p.Title, pattern) ||
                (p.VideoTitle != null && EF.Functions.Like(p.VideoTitle, pattern)) ||
                (p.SiteTitle != null && EF.Functions.Like(p.SiteTitle, pattern)));
        }

        if (siteId.HasValue)
            q = q.Where(p => p.PrdbSiteId == siteId.Value);

        if (hasLinkedVideo.HasValue)
            q = hasLinkedVideo.Value
                ? q.Where(p => p.PrdbVideoId != null)
                : q.Where(p => p.PrdbVideoId == null);

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenByDescending(p => p.SyncedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PrdbPreDbListResponse
            {
                Id             = p.Id,
                Title          = p.Title,
                CreatedAtUtc   = p.CreatedAtUtc,
                VideoId        = p.PrdbVideoId,
                VideoTitle     = p.VideoTitle,
                SiteId         = p.PrdbSiteId,
                SiteTitle      = p.SiteTitle,
                ReleaseDate    = p.ReleaseDate,
                HasLinkedVideo = p.PrdbVideoId != null,
            })
            .ToListAsync();

        return Ok(new { items, total });
    }

    [HttpGet("filter-options")]
    [EndpointSummary("Get filter options for PreDB entries")]
    [EndpointDescription("Returns the distinct sites present in the local PreDB entry cache.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFilterOptions()
    {
        var sites = await db.PrdbPreDbEntries
            .AsNoTracking()
            .Where(p => p.PrdbSiteId != null && p.SiteTitle != null)
            .Select(p => new { Id = p.PrdbSiteId!.Value, Title = p.SiteTitle! })
            .Distinct()
            .OrderBy(s => s.Title)
            .ToListAsync();

        return Ok(new { sites });
    }
}
