using System.ComponentModel.DataAnnotations;

namespace porganizer.Api.Features.Indexers;

public class TestIndexerRequest
{
    [Required]
    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ApiPath { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string ApiKey { get; set; } = string.Empty;
}
