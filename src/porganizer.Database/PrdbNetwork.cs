using System.ComponentModel.DataAnnotations;

namespace porganizer.Database;

public class PrdbNetwork
{
    public Guid Id { get; set; }

    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    public DateTime SyncedAtUtc { get; set; }

    public ICollection<PrdbSite> Sites { get; set; } = [];
}
