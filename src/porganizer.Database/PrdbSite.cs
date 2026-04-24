using System.ComponentModel.DataAnnotations;

namespace porganizer.Database;

public class PrdbSite
{
    public Guid Id { get; set; }

    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    public Guid? NetworkId { get; set; }
    public PrdbNetwork? Network { get; set; }

    public bool IsFavorite { get; set; }
    public DateTime? FavoritedAtUtc { get; set; }

    public DateTime SyncedAtUtc { get; set; }

    public ICollection<PrdbPreDbEntry> PreDbEntries { get; set; } = [];
    public ICollection<PrdbVideo> Videos { get; set; } = [];
}
