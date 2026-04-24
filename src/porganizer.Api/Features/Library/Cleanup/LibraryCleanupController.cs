using Microsoft.AspNetCore.Mvc;

namespace porganizer.Api.Features.Library.Cleanup;

[ApiController]
[Route("api/library-cleanup")]
[Produces("application/json")]
public class LibraryCleanupController(ILibraryCleanupService cleanupService) : ControllerBase
{
    [HttpGet("uploaded-files")]
    [EndpointSummary("Preview files eligible for cleanup")]
    [EndpointDescription(
        "Returns all library files that have been fully uploaded to prdb.net (all 5 preview images " +
        "and the sprite sheet) and still have at least one file on disk (video, preview images, or " +
        "sprite sheet). Includes a total count and total bytes that would be freed from video files.")]
    [ProducesResponseType(typeof(CleanupPreviewResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEligibleFiles(CancellationToken ct)
    {
        var result = await cleanupService.GetEligibleFilesAsync(ct);
        return Ok(result);
    }

    [HttpPost("delete-uploaded-files")]
    [EndpointSummary("Delete files that have been fully uploaded to prdb.net")]
    [EndpointDescription(
        "Deletes the video file, preview images, and sprite sheet from disk for all library files " +
        "that have been fully uploaded to prdb.net. Returns the number of files processed and the " +
        "total bytes freed from deleted video files.")]
    [ProducesResponseType(typeof(CleanupDeleteResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteEligibleFiles(CancellationToken ct)
    {
        var result = await cleanupService.DeleteEligibleFilesAsync(ct);
        return Ok(result);
    }
}
