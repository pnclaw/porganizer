using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.FolderMappings;

[ApiController]
[Route("api/folder-mappings")]
[Produces("application/json")]
public class FolderMappingsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List all folder mappings")]
    [ProducesResponseType(typeof(IEnumerable<FolderMappingResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var mappings = await db.FolderMappings
            .OrderBy(f => f.OriginalFolder)
            .ToListAsync();
        return Ok(mappings.Select(ToResponse));
    }

    [HttpPost]
    [Consumes("application/json")]
    [EndpointSummary("Create a folder mapping")]
    [ProducesResponseType(typeof(FolderMappingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] FolderMappingRequest request)
    {
        var duplicate = await db.FolderMappings.AnyAsync(f =>
            f.OriginalFolder == request.OriginalFolder ||
            f.MappedToFolder == request.MappedToFolder);

        if (duplicate)
            return Conflict("A mapping with the same original or mapped folder already exists.");

        var mapping = new FolderMapping
        {
            Id = Guid.NewGuid(),
            OriginalFolder = request.OriginalFolder,
            MappedToFolder = request.MappedToFolder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.FolderMappings.Add(mapping);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), ToResponse(mapping));
    }

    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [EndpointSummary("Update a folder mapping")]
    [ProducesResponseType(typeof(FolderMappingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] FolderMappingRequest request)
    {
        var mapping = await db.FolderMappings.FindAsync(id);
        if (mapping is null) return NotFound();

        var duplicate = await db.FolderMappings.AnyAsync(f =>
            f.Id != id &&
            (f.OriginalFolder == request.OriginalFolder ||
             f.MappedToFolder == request.MappedToFolder));

        if (duplicate)
            return Conflict("A mapping with the same original or mapped folder already exists.");

        mapping.OriginalFolder = request.OriginalFolder;
        mapping.MappedToFolder = request.MappedToFolder;
        mapping.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(ToResponse(mapping));
    }

    [HttpDelete("{id:guid}")]
    [EndpointSummary("Delete a folder mapping")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var mapping = await db.FolderMappings.FindAsync(id);
        if (mapping is null) return NotFound();

        db.FolderMappings.Remove(mapping);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static FolderMappingResponse ToResponse(FolderMapping f) => new()
    {
        Id = f.Id,
        OriginalFolder = f.OriginalFolder,
        MappedToFolder = f.MappedToFolder,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt,
    };
}
