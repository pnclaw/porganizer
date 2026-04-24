using System.ComponentModel.DataAnnotations;
using porganizer.Database.Common;

namespace porganizer.Database;

public class IndexerRow : BaseEntity
{
    public Guid Id { get; set; }

    public Guid IndexerId { get; set; }
    public Indexer Indexer { get; set; } = null!;

    [MaxLength(2000)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string NzbId { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string NzbUrl { get; set; } = string.Empty;

    public long NzbSize { get; set; }

    public DateTime? NzbPublishedAt { get; set; }

    public long? FileSize { get; set; }

    public int Category { get; set; }
}
