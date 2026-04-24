using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb;

[ApiController]
[Route("api/prdb-actors")]
[Produces("application/json")]
public class PrdbActorsController(AppDbContext db, PrdbFavoritesService favoritesService) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List prdb actors")]
    [EndpointDescription("Returns a paged list of synced prdb actors with aliases. Optionally filter by search term or favorites.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] bool? favoritesOnly,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var q = db.PrdbActors.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(a => EF.Functions.Like(a.Name, $"%{search}%"));

        if (favoritesOnly == true)
            q = q.Where(a => a.IsFavorite);

        var total = await q.CountAsync();

        var items = await q
            .OrderBy(a => a.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new PrdbActorResponse
            {
                Id              = a.Id,
                Name            = a.Name,
                Gender          = a.Gender,
                Nationality     = a.Nationality,
                Birthday        = a.Birthday,
                IsFavorite      = a.IsFavorite,
                FavoritedAtUtc  = a.FavoritedAtUtc,
                Aliases         = a.Aliases.Select(x => x.Name).ToList(),
                ProfileImageUrl = a.Images.Select(i => i.Url).FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(new { items, total });
    }

    [HttpPost("{id:guid}/favorite")]
    [EndpointSummary("Add a favorite actor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddFavorite(Guid id, CancellationToken ct)
    {
        if (!await db.PrdbActors.AnyAsync(a => a.Id == id)) return NotFound();
        await favoritesService.SetActorFavoriteAsync(id, true, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/favorite")]
    [EndpointSummary("Remove a favorite actor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFavorite(Guid id, CancellationToken ct)
    {
        if (!await db.PrdbActors.AnyAsync(a => a.Id == id)) return NotFound();
        await favoritesService.SetActorFavoriteAsync(id, false, ct);
        return NoContent();
    }
}
