using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.Prdb.Sync;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb;

[ApiController]
[Route("api/prdb-sites")]
[Produces("application/json")]
public class PrdbSitesController(AppDbContext db, PrdbFavoritesService favoritesService, IServiceScopeFactory scopeFactory) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List prdb sites")]
    [EndpointDescription("Returns a paged list of synced prdb sites with video counts. Optionally filter by search term or favorites.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] bool? favoritesOnly,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var q = db.PrdbSites
            .Include(s => s.Network)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(s => EF.Functions.Like(s.Title, $"%{search}%"));

        if (favoritesOnly == true)
            q = q.Where(s => s.IsFavorite);

        var total = await q.CountAsync();

        var items = await q
            .OrderBy(s => s.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new PrdbSiteResponse
            {
                Id               = s.Id,
                Title            = s.Title,
                Url              = s.Url,
                NetworkId        = s.NetworkId,
                NetworkTitle     = s.Network != null ? s.Network.Title : null,
                IsFavorite       = s.IsFavorite,
                FavoritedAtUtc   = s.FavoritedAtUtc,
                VideoCount       = s.Videos.Count,
                ThumbnailCdnPath = s.Videos
                    .OrderByDescending(v => v.ReleaseDate)
                    .SelectMany(v => v.Images)
                    .Select(i => i.CdnPath)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(new { items, total });
    }

    [HttpPost("{id:guid}/favorite")]
    [EndpointSummary("Add a favorite site")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddFavorite(Guid id, CancellationToken ct)
    {
        if (!await db.PrdbSites.AnyAsync(s => s.Id == id)) return NotFound();
        await favoritesService.SetSiteFavoriteAsync(id, true, ct);

        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sync = scope.ServiceProvider.GetRequiredService<PrdbSyncService>();
            await sync.SyncSiteVideosAsync(id);
        });

        return NoContent();
    }

    [HttpDelete("{id:guid}/favorite")]
    [EndpointSummary("Remove a favorite site")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFavorite(Guid id, CancellationToken ct)
    {
        if (!await db.PrdbSites.AnyAsync(s => s.Id == id)) return NotFound();
        await favoritesService.SetSiteFavoriteAsync(id, false, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/videos")]
    [EndpointSummary("List videos for a site")]
    [EndpointDescription("Returns a paged list of synced videos for the given site including pre-names and actor count.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVideos(
        Guid id,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!await db.PrdbSites.AnyAsync(s => s.Id == id)) return NotFound();

        var q = db.PrdbVideos
            .Where(v => v.SiteId == id)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(v => EF.Functions.Like(v.Title, $"%{search}%"));

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(v => v.ReleaseDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new PrdbVideoResponse
            {
                Id          = v.Id,
                Title       = v.Title,
                ReleaseDate = v.ReleaseDate,
                ActorCount  = v.VideoActors.Count,
                PreNames    = v.PreDbEntries
                    .Select(p => new PrdbPreNameResponse { Id = p.Id, Title = p.Title })
                    .ToList(),
            })
            .ToListAsync();

        return Ok(new { items, total });
    }
}
