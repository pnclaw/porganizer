using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb;

[ApiController]
[Route("api/prdb-prenames")]
[Produces("application/json")]
public class PrdbPreNamesController(AppDbContext db) : ControllerBase
{
    [HttpGet("search")]
    [EndpointSummary("Search prenames")]
    [EndpointDescription("Returns prenames grouped by video. Requires at least 3 characters in 'q' or a release date range (releaseDateFrom / releaseDateTo).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] DateOnly? releaseDateFrom,
        [FromQuery] DateOnly? releaseDateTo)
    {
        var hasQuery = !string.IsNullOrWhiteSpace(q) && q.Trim().Length >= 3;
        var hasDateRange = releaseDateFrom.HasValue || releaseDateTo.HasValue;

        if (!hasQuery && !hasDateRange)
            return BadRequest("Provide at least 3 characters in 'q' or a release date range.");

        var preNamesQuery = db.PrdbPreDbEntries
            .Where(p => p.PrdbVideoId != null)
            .Include(p => p.Video!)
            .ThenInclude(v => v.Site)
            .AsQueryable();

        if (hasQuery)
            preNamesQuery = preNamesQuery.Where(p => EF.Functions.Like(p.Title, $"%{q!.Trim()}%"));

        if (releaseDateFrom.HasValue)
            preNamesQuery = preNamesQuery.Where(p => p.Video!.ReleaseDate >= releaseDateFrom);

        if (releaseDateTo.HasValue)
            preNamesQuery = preNamesQuery.Where(p => p.Video!.ReleaseDate <= releaseDateTo);

        var flat = await preNamesQuery
            .OrderBy(p => p.Video!.Site!.Title)
            .ThenBy(p => p.Video!.Title)
            .ThenBy(p => p.Title)
            .Select(p => new
            {
                PreNameId    = p.Id,
                PreNameTitle = p.Title,
                VideoId      = p.Video!.Id,
                VideoTitle   = p.Video!.Title,
                SiteId       = p.Video!.Site!.Id,
                SiteTitle    = p.Video!.Site!.Title,
            })
            .ToListAsync();

        var groups = flat
            .GroupBy(p => p.VideoId)
            .Take(500)
            .Select(g => new
            {
                videoId    = g.Key,
                videoTitle = g.First().VideoTitle,
                siteId     = g.First().SiteId,
                siteTitle  = g.First().SiteTitle,
                preNames   = g.Select(p => new { id = p.PreNameId, title = p.PreNameTitle }).ToList(),
            })
            .ToList();

        return Ok(new { items = groups, totalGroups = groups.Count });
    }
}
