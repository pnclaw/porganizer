using System.ComponentModel.DataAnnotations;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.Indexers;

public class CreateIndexerRequest
{
    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    [Required]
    public ParsingType ParsingType { get; set; }

    public bool IsEnabled { get; set; }

    [MaxLength(2000)]
    public string ApiKey { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ApiPath { get; set; } = string.Empty;

    [Range(1, 3650)]
    public int BackfillDays { get; set; } = 30;
}
