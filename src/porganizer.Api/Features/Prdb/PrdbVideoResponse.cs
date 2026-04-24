namespace porganizer.Api.Features.Prdb;

public class PrdbVideoListResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly? ReleaseDate { get; set; }
    public Guid SiteId { get; set; }
    public string SiteTitle { get; set; } = string.Empty;
    public string? ThumbnailCdnPath { get; set; }
    public int ActorCount { get; set; }
    public bool IsWanted { get; set; }
    public bool? IsFulfilled { get; set; }
    public bool HasIndexerMatch { get; set; }
}

public class PrdbVideoResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly? ReleaseDate { get; set; }
    public int ActorCount { get; set; }
    public List<PrdbPreNameResponse> PreNames { get; set; } = [];
}

public class PrdbPreNameResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
}
