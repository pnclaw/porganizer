using System.ComponentModel.DataAnnotations;

namespace porganizer.Database;

public class PrdbPreDbEntry
{
    public Guid Id { get; set; }

    [MaxLength(1000)]
    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public Guid? PrdbVideoId { get; set; }
    public PrdbVideo? Video { get; set; }

    public Guid? PrdbSiteId { get; set; }
    public PrdbSite? Site { get; set; }

    [MaxLength(1000)]
    public string? VideoTitle { get; set; }

    [MaxLength(500)]
    public string? SiteTitle { get; set; }

    public DateOnly? ReleaseDate { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}
