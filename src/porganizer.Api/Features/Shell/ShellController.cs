using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace porganizer.Api.Features.Shell;

[ApiController]
[Route("api/shell")]
[Produces("application/json")]
public class ShellController : ControllerBase
{
    [HttpPost("open")]
    [EndpointSummary("Open a path in the system shell")]
    [EndpointDescription("Opens a file or directory using the OS default handler (explorer, Finder, xdg-open).")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Open([FromBody] ShellOpenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return BadRequest();

        Process.Start(new ProcessStartInfo
        {
            FileName        = request.Path,
            UseShellExecute = true,
        });

        return NoContent();
    }
}

public class ShellOpenRequest
{
    public string Path { get; set; } = string.Empty;
}
