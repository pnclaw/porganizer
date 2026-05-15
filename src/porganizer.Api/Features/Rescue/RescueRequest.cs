using System.ComponentModel.DataAnnotations;

namespace porganizer.Api.Features.Rescue;

public class RescueRequest
{
    [Required]
    public string Folder { get; set; } = string.Empty;
}
