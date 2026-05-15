using Microsoft.AspNetCore.Mvc;

namespace porganizer.Api.Features.Rescue;

[ApiController]
[Route("api/rescue")]
[Produces("application/json")]
[Consumes("application/json")]
public class RescueController(
    IRescuePreviewService previewService,
    IRescueExecuteService executeService) : ControllerBase
{
    [HttpPost("preview")]
    [EndpointSummary("Preview rescue matches")]
    [EndpointDescription("Scans direct subfolders of the given path and shows which ones can be matched to an indexed video. Does not move any files.")]
    [ProducesResponseType(typeof(RescuePreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview([FromBody] RescueRequest request)
    {
        if (!Directory.Exists(request.Folder))
            return BadRequest($"Folder not found: {request.Folder}");

        return Ok(await previewService.PreviewAsync(request.Folder));
    }

    [HttpPost("execute")]
    [EndpointSummary("Execute rescue")]
    [EndpointDescription("Scans direct subfolders of the given path, matches each to an indexed video, and moves matched files to the configured target folder.")]
    [ProducesResponseType(typeof(RescueExecuteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Execute([FromBody] RescueRequest request, CancellationToken ct)
    {
        if (!Directory.Exists(request.Folder))
            return BadRequest($"Folder not found: {request.Folder}");

        return Ok(await executeService.ExecuteAsync(request.Folder, ct));
    }
}
