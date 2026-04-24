namespace porganizer.Api.Features.Prdb;

public class PrdbVideoDetailResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly? ReleaseDate { get; set; }
    public Guid SiteId { get; set; }
    public string SiteTitle { get; set; } = string.Empty;
    public string? SiteUrl { get; set; }
    public List<string> ImageCdnPaths { get; set; } = [];
    public List<PrdbVideoDetailActorResponse> Actors { get; set; } = [];
    public List<string> PreNames { get; set; } = [];
    public bool? IsFulfilled { get; set; }
}

public class PrdbVideoDetailActorResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
