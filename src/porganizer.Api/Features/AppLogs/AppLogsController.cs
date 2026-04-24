using Microsoft.AspNetCore.Mvc;

namespace porganizer.Api.Features.AppLogs;

[ApiController]
[Route("api/app-logs")]
[Produces("application/json")]
public class AppLogsController(IAppLogsService service) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List log files")]
    [EndpointDescription("Returns all application log files in the logs directory, sorted newest first.")]
    [ProducesResponseType(typeof(IReadOnlyList<AppLogFileInfo>), StatusCodes.Status200OK)]
    public IActionResult List() => Ok(service.ListFiles());

    [HttpGet("{filename}")]
    [EndpointSummary("Get log file lines")]
    [EndpointDescription("Returns lines from a single log file. Optionally filter by search text (case-insensitive) and/or one or more Serilog level codes (VRB, DBG, INF, WRN, ERR, FTL) passed as repeated 'level' query parameters.")]
    [ProducesResponseType(typeof(AppLogLinesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetLines(string filename, [FromQuery] string? search, [FromQuery(Name = "level")] List<string>? levels)
    {
        try
        {
            return Ok(service.GetLines(filename, search, levels));
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid filename.");
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete]
    [EndpointSummary("Delete log files")]
    [EndpointDescription("Deletes log files according to the retention policy: 'all' removes every file, 'last7' removes files older than 7 days, 'today' removes all but today's file.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Delete([FromQuery] string retain)
    {
        var policy = retain switch
        {
            "all"   => (LogRetentionPolicy?)LogRetentionPolicy.All,
            "last7" => LogRetentionPolicy.Last7,
            "today" => LogRetentionPolicy.Today,
            _       => null,
        };

        if (policy is null)
            return BadRequest("retain must be 'all', 'last7', or 'today'.");

        service.DeleteFiles(policy.Value);
        return NoContent();
    }
}
