using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.DownloadClients;

[ApiController]
[Route("api/download-clients")]
[Produces("application/json")]
public class DownloadClientsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List all download clients")]
    [ProducesResponseType(typeof(IEnumerable<DownloadClientResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var clients = await db.DownloadClients
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return Ok(clients.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    [EndpointSummary("Get download client by ID")]
    [ProducesResponseType(typeof(DownloadClientResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var client = await db.DownloadClients.FindAsync(id);
        return client is null ? NotFound() : Ok(ToResponse(client));
    }

    [HttpPost]
    [Consumes("application/json")]
    [EndpointSummary("Create download client")]
    [ProducesResponseType(typeof(DownloadClientResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateDownloadClientRequest request)
    {
        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            ClientType = request.ClientType,
            Host = request.Host,
            Port = request.Port,
            UseSsl = request.UseSsl,
            ApiKey = request.ApiKey,
            Username = request.Username,
            Password = request.Password,
            Category = request.Category,
            IsEnabled = request.IsEnabled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.DownloadClients.Add(client);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = client.Id }, ToResponse(client));
    }

    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [EndpointSummary("Update download client")]
    [ProducesResponseType(typeof(DownloadClientResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDownloadClientRequest request)
    {
        var client = await db.DownloadClients.FindAsync(id);
        if (client is null) return NotFound();

        client.Title = request.Title;
        client.ClientType = request.ClientType;
        client.Host = request.Host;
        client.Port = request.Port;
        client.UseSsl = request.UseSsl;
        client.ApiKey = request.ApiKey;
        client.Username = request.Username;
        client.Password = request.Password;
        client.Category = request.Category;
        client.IsEnabled = request.IsEnabled;
        client.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(ToResponse(client));
    }

    [HttpDelete("{id:guid}")]
    [EndpointSummary("Delete download client")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var client = await db.DownloadClients.FindAsync(id);
        if (client is null) return NotFound();

        db.DownloadClients.Remove(client);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/send")]
    [Consumes("application/json")]
    [EndpointSummary("Send NZB to download client")]
    [EndpointDescription("Sends an NZB URL to the specified download client for download.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Send(
        Guid id,
        [FromBody] SendNzbRequest request,
        [FromServices] DownloadClientSender sender)
    {
        var client = await db.DownloadClients.FindAsync(id);
        if (client is null) return NotFound();
        if (!client.IsEnabled) return BadRequest("Download client is disabled.");

        var sw = Stopwatch.StartNew();
        var (success, message, clientItemId) = await sender.SendAsync(client, request.NzbUrl, request.Name);
        sw.Stop();

        db.IndexerApiRequests.Add(new IndexerApiRequest
        {
            Id = Guid.NewGuid(),
            IndexerId = request.IndexerId,
            RequestType = IndexerRequestType.Grab,
            OccurredAt = DateTime.UtcNow,
            Success = success,
            HttpStatusCode = null, // request to indexer is made by the download client
            ResponseTimeMs = (int)sw.ElapsedMilliseconds,
        });

        Guid? downloadLogId = null;
        if (success && request.IndexerRowId.HasValue)
        {
            var log = new DownloadLog
            {
                Id               = Guid.NewGuid(),
                IndexerRowId     = request.IndexerRowId.Value,
                DownloadClientId = id,
                NzbName          = request.Name,
                NzbUrl           = request.NzbUrl,
                ClientItemId     = clientItemId,
                Status           = DownloadStatus.Queued,
                CreatedAt        = DateTime.UtcNow,
                UpdatedAt        = DateTime.UtcNow,
            };
            db.DownloadLogs.Add(log);
            downloadLogId = log.Id;
        }

        await db.SaveChangesAsync();

        return Ok(new { success, message, downloadLogId });
    }

    [HttpPost("test")]
    [Consumes("application/json")]
    [EndpointSummary("Test download client connection")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Test(
        [FromBody] TestDownloadClientRequest request,
        [FromServices] DownloadClientTester tester)
    {
        var (success, message) = await tester.TestAsync(
            request.ClientType, request.Host, request.Port, request.UseSsl,
            request.ApiKey, request.Username, request.Password);

        return Ok(new { success, message });
    }

    private static DownloadClientResponse ToResponse(DownloadClient c) => new()
    {
        Id = c.Id,
        Title = c.Title,
        ClientType = (int)c.ClientType,
        Host = c.Host,
        Port = c.Port,
        UseSsl = c.UseSsl,
        ApiKey = c.ApiKey,
        Username = c.Username,
        Password = c.Password,
        Category = c.Category,
        IsEnabled = c.IsEnabled,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };
}
