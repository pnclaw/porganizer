using System.ComponentModel.DataAnnotations;

namespace porganizer.Database;

public class PrdbVideoFilehash
{
    public Guid Id { get; set; }

    public Guid? VideoId { get; set; }

    [MaxLength(1000)]
    public string Filename { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? OsHash { get; set; }

    [MaxLength(255)]
    public string? PHash { get; set; }

    public long Filesize { get; set; }

    public int SubmissionCount { get; set; }

    public bool IsVerified { get; set; }

    public DateTime PrdbCreatedAtUtc { get; set; }
    public DateTime PrdbUpdatedAtUtc { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}
