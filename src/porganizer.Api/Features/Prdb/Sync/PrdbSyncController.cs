using Microsoft.AspNetCore.Mvc;

namespace porganizer.Api.Features.Prdb.Sync;

[ApiController]
[Route("api/prdb-sync")]
[Produces("application/json")]
public class PrdbSyncController(PrdbSyncService syncService) : ControllerBase
{
    [HttpPost]
    [EndpointSummary("Sync all prdb data")]
    [EndpointDescription("Syncs all sites/networks and videos (favorite sites + latest 1500 global) from prdb.net.")]
    [ProducesResponseType(typeof(PrdbSyncResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        var result = await syncService.SyncAsync(ct);
        return Ok(result);
    }
}
