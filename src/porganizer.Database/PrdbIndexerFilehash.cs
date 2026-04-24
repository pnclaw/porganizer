using System.ComponentModel.DataAnnotations;

namespace porganizer.Database;

public class PrdbIndexerFilehash
{
    public Guid Id { get; set; }

    public int IndexerSource { get; set; }

    [MaxLength(255)]
    public string IndexerId { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Filename { get; set; } = string.Empty;

    [MaxLength(255)]
    public string OsHash { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? PHash { get; set; }

    public long Filesize { get; set; }

    public int SubmissionCount { get; set; }

    public bool IsVerified { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public DateTime PrdbCreatedAtUtc { get; set; }
    public DateTime PrdbUpdatedAtUtc { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}
