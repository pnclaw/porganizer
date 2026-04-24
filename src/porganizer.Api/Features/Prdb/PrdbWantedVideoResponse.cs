namespace porganizer.Api.Features.Prdb;

public class PrdbWantedVideoResponse
{
    public Guid VideoId { get; set; }
    public string VideoTitle { get; set; } = string.Empty;
    public Guid SiteId { get; set; }
    public string SiteTitle { get; set; } = string.Empty;
    public DateOnly? ReleaseDate { get; set; }
    public string? ThumbnailCdnPath { get; set; }
    public bool IsFulfilled { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public int? FulfilledInQuality { get; set; }
    public DateTime AddedAtUtc { get; set; }
}
