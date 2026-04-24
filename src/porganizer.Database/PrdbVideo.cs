using System.ComponentModel.DataAnnotations;

namespace porganizer.Database;

public class PrdbVideo
{
    public Guid Id { get; set; }

    [MaxLength(1000)]
    public string Title { get; set; } = string.Empty;

    public DateOnly? ReleaseDate { get; set; }

    public Guid SiteId { get; set; }
    public PrdbSite Site { get; set; } = null!;

    public DateTime PrdbCreatedAtUtc { get; set; }
    public DateTime PrdbUpdatedAtUtc { get; set; }
    public DateTime SyncedAtUtc { get; set; }
    public DateTime? DetailSyncedAtUtc { get; set; }

    public ICollection<PrdbPreDbEntry> PreDbEntries { get; set; } = [];
    public ICollection<PrdbVideoImage> Images { get; set; } = [];
    public ICollection<PrdbVideoActor> VideoActors { get; set; } = [];
}
