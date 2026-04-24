namespace porganizer.Api.Features.Indexers.Search;

public class UsenetSearchQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? Search { get; set; }
    public Guid[]? IndexerIds { get; set; }
    public bool PreviewMode { get; set; }
}
