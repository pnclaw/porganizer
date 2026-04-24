namespace porganizer.Api.Features.Prdb;

public class PrdbSiteResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public Guid? NetworkId { get; set; }
    public string? NetworkTitle { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime? FavoritedAtUtc { get; set; }
    public int VideoCount { get; set; }
    public string? ThumbnailCdnPath { get; set; }
}
