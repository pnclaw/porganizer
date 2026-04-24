using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;
using System.Net;
using System.Net.Http.Json;

namespace porganizer.Api.Features.Prdb;

[ApiController]
[Route("api/prdb-wanted-videos")]
[Produces("application/json")]
public class PrdbWantedVideosController(AppDbContext db, IHttpClientFactory httpClientFactory) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List wanted videos")]
    [EndpointDescription("Returns a paged list of wanted videos. Optionally filter by search, fulfilment status, site, or actor.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] bool? isFulfilled,
        [FromQuery] Guid? siteId,
        [FromQuery] Guid? actorId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var q = db.PrdbWantedVideos.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(w => EF.Functions.Like(w.Video!.Title, $"%{search}%"));

        if (isFulfilled.HasValue)
            q = q.Where(w => w.IsFulfilled == isFulfilled.Value);

        if (siteId.HasValue)
            q = q.Where(w => w.Video!.SiteId == siteId.Value);

        if (actorId.HasValue)
            q = q.Where(w => w.Video!.VideoActors.Any(va => va.ActorId == actorId.Value));

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(w => w.PrdbCreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new PrdbWantedVideoResponse
            {
                VideoId            = w.VideoId,
                VideoTitle         = w.Video!.Title,
                SiteId             = w.Video!.SiteId,
                SiteTitle          = w.Video!.Site.Title,
                ReleaseDate        = w.Video!.ReleaseDate,
                ThumbnailCdnPath   = w.Video!.Images.Select(i => i.CdnPath).FirstOrDefault(),
                IsFulfilled        = w.IsFulfilled,
                FulfilledAtUtc     = w.FulfilledAtUtc,
                FulfilledInQuality = w.FulfilledInQuality,
                AddedAtUtc         = w.PrdbCreatedAtUtc,
            })
            .ToListAsync();

        return Ok(new { items, total });
    }

    [HttpGet("filter-options")]
    [EndpointSummary("Get filter options for wanted videos")]
    [EndpointDescription("Returns the distinct sites and actors present in the wanted list, for use in filter dropdowns.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFilterOptions()
    {
        var siteIds = await db.PrdbWantedVideos
            .Select(w => w.Video!.SiteId)
            .Distinct()
            .ToListAsync();

        var sites = await db.PrdbSites
            .Where(s => siteIds.Contains(s.Id))
            .OrderBy(s => s.Title)
            .Select(s => new { s.Id, s.Title })
            .ToListAsync();

        var actorIds = await db.PrdbVideoActors
            .Where(va => db.PrdbWantedVideos.Select(w => w.VideoId).Contains(va.VideoId))
            .Select(va => va.ActorId)
            .Distinct()
            .ToListAsync();

        var actors = await db.PrdbActors
            .Where(a => actorIds.Contains(a.Id))
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync();

        return Ok(new { sites, actors });
    }

    [HttpPost("{videoId:guid}")]
    [EndpointSummary("Add a wanted video")]
    [EndpointDescription("Adds the video to the prdb wanted list and upserts the local record. Idempotent — safe to call if already wanted.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Add(Guid videoId, CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);

        var prdbResponse = await http.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"wanted-videos/{videoId}"), ct);
        if (prdbResponse.StatusCode == System.Net.HttpStatusCode.NotFound) return NotFound();
        prdbResponse.EnsureSuccessStatusCode();

        var existing = await db.PrdbWantedVideos.FindAsync([videoId], ct);
        if (existing is null)
        {
            var now = DateTime.UtcNow;
            db.PrdbWantedVideos.Add(new PrdbWantedVideo
            {
                VideoId            = videoId,
                IsFulfilled        = false,
                PrdbCreatedAtUtc   = now,
                PrdbUpdatedAtUtc   = now,
                SyncedAtUtc        = now,
            });
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    [HttpPatch("{videoId:guid}")]
    [EndpointSummary("Update a wanted video")]
    [EndpointDescription("Updates the fulfilment state of a wanted video in both prdb and the local database.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid videoId, [FromBody] UpdateWantedVideoRequest request, CancellationToken ct)
    {
        var entry = await db.PrdbWantedVideos.FindAsync([videoId], ct);
        if (entry is null) return NotFound();

        var now            = request.IsFulfilled ? (DateTime?)DateTime.UtcNow : null;
        entry.IsFulfilled           = request.IsFulfilled;
        entry.FulfilledAtUtc        = request.IsFulfilled ? entry.FulfilledAtUtc ?? now : null;
        entry.FulfilledInQuality    = request.IsFulfilled ? entry.FulfilledInQuality : null;
        entry.FulfillmentExternalId = request.IsFulfilled ? entry.FulfillmentExternalId : null;
        entry.FulfillmentByApp      = request.IsFulfilled ? entry.FulfillmentByApp : null;

        var settings = await db.GetSettingsAsync(ct);
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);

        (await http.PutAsJsonAsync($"wanted-videos/{videoId}", new
        {
            isFulfilled           = entry.IsFulfilled,
            fulfilledAtUtc        = entry.FulfilledAtUtc,
            fulfilledInQuality    = entry.FulfilledInQuality,
            fulfillmentExternalId = entry.FulfillmentExternalId,
            fulfillmentByApp      = entry.FulfillmentByApp,
        }, ct)).EnsureSuccessStatusCode();

        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpDelete("{videoId:guid}")]
    [EndpointSummary("Remove a wanted video")]
    [EndpointDescription("Removes the video from the prdb wanted list and from the local database.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid videoId, CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);

        var prdbResponse = await http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"wanted-videos/{videoId}"), ct);
        if (prdbResponse.StatusCode != HttpStatusCode.NotFound)
            prdbResponse.EnsureSuccessStatusCode();

        var entry = await db.PrdbWantedVideos.FindAsync([videoId], ct);
        if (entry is not null)
        {
            db.PrdbWantedVideos.Remove(entry);
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }
}

public record UpdateWantedVideoRequest(bool IsFulfilled);
