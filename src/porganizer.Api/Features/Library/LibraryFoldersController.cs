using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Library;

[ApiController]
[Route("api/library-folders")]
[Produces("application/json")]
public class LibraryFoldersController(
    AppDbContext db,
    IServiceScopeFactory scopeFactory,
    ILogger<LibraryFoldersController> logger) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List all library folders")]
    [ProducesResponseType(typeof(IEnumerable<LibraryFolderResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var folders = await db.LibraryFolders
            .OrderBy(f => f.Path)
            .ToListAsync();

        return Ok(folders.Select(ToResponse));
    }

    [HttpPost]
    [Consumes("application/json")]
    [EndpointSummary("Add a library folder")]
    [ProducesResponseType(typeof(LibraryFolderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] LibraryFolderRequest request)
    {
        var duplicate = await db.LibraryFolders.AnyAsync(f => f.Path == request.Path);
        if (duplicate)
            return Conflict("A library folder with this path already exists.");

        var folder = new LibraryFolder
        {
            Id = Guid.NewGuid(),
            Path = request.Path,
            Label = request.Label,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.LibraryFolders.Add(folder);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), ToResponse(folder));
    }

    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [EndpointSummary("Update a library folder label")]
    [ProducesResponseType(typeof(LibraryFolderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] LibraryFolderRequest request)
    {
        var folder = await db.LibraryFolders.FindAsync(id);
        if (folder is null) return NotFound();

        folder.Label = request.Label;
        folder.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(ToResponse(folder));
    }

    [HttpDelete("{id:guid}")]
    [EndpointSummary("Remove a library folder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var folder = await db.LibraryFolders.FindAsync(id);
        if (folder is null) return NotFound();

        db.LibraryFolders.Remove(folder);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/index")]
    [EndpointSummary("Trigger indexing for a single folder")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IndexFolder(Guid id)
    {
        if (!await db.LibraryFolders.AnyAsync(f => f.Id == id))
            return NotFound();

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<LibraryIndexingService>();
            try
            {
                await svc.IndexFolderAsync(id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LibraryFoldersController: background index failed for folder {FolderId}", id);
            }
        });

        return Accepted();
    }

    [HttpPost("index-all")]
    [EndpointSummary("Trigger indexing for all library folders")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult IndexAll()
    {
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<LibraryIndexingService>();
            try
            {
                await svc.IndexAllAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LibraryFoldersController: background index-all failed");
            }
        });

        return Accepted();
    }

    private static LibraryFolderResponse ToResponse(LibraryFolder f) => new()
    {
        Id = f.Id,
        Path = f.Path,
        Label = f.Label,
        FileCount = f.FileCount,
        MatchedCount = f.MatchedCount,
        LastIndexedAtUtc = f.LastIndexedAtUtc,
        IndexingStartedAtUtc = f.IndexingStartedAtUtc,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt,
    };
}

public class LibraryFolderRequest
{
    public string Path { get; set; } = string.Empty;
    public string? Label { get; set; }
}

public class LibraryFolderResponse
{
    public Guid Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int FileCount { get; set; }
    public int MatchedCount { get; set; }
    public DateTime? LastIndexedAtUtc { get; set; }
    public DateTime? IndexingStartedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
