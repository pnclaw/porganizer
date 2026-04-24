using System.ComponentModel.DataAnnotations;

namespace porganizer.Api.Features.FolderMappings;

public class FolderMappingRequest
{
    [Required]
    [MaxLength(2000)]
    public string OriginalFolder { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string MappedToFolder { get; set; } = string.Empty;
}
