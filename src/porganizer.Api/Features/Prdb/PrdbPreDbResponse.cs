namespace porganizer.Api.Features.Prdb;

public class PrdbPreDbListResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public Guid? VideoId { get; set; }
    public string? VideoTitle { get; set; }
    public Guid? SiteId { get; set; }
    public string? SiteTitle { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public bool HasLinkedVideo { get; set; }
}
