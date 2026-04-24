namespace porganizer.Api.Features.FolderMappings;

public class FolderMappingResponse
{
    public Guid Id { get; set; }
    public string OriginalFolder { get; set; } = string.Empty;
    public string MappedToFolder { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
