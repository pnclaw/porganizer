namespace porganizer.Api.Features.Indexers;

public class IndexerRowResponse
{
    public Guid Id { get; set; }
    public Guid IndexerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string NzbId { get; set; } = string.Empty;
    public string NzbUrl { get; set; } = string.Empty;
    public long NzbSize { get; set; }
    public DateTime? NzbPublishedAt { get; set; }
    public long? FileSize { get; set; }
    public int Category { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? PrdbVideoId { get; set; }
}
