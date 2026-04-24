namespace porganizer.Api.Features.Indexers;

public class IndexerRowsQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? Search { get; set; }
    public int[]? Categories { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public long? MinSize { get; set; }
    public long? MaxSize { get; set; }
    public bool? HasVideoLink { get; set; }
}
