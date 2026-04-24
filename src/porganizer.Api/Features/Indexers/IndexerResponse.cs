namespace porganizer.Api.Features.Indexers;

public class IndexerResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>Parsing protocol integer value (0 = Newznab).</summary>
    public int ParsingType { get; set; }

    public bool IsEnabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string ApiPath { get; set; } = string.Empty;
    public int BackfillDays { get; set; }
    public DateTime? BackfillStartedAtUtc { get; set; }
    public DateTime? BackfillCutoffUtc { get; set; }
    public DateTime? BackfillCompletedAtUtc { get; set; }
    public DateTime? BackfillLastRunAtUtc { get; set; }
    public int? BackfillCurrentOffset { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
