using System.ComponentModel.DataAnnotations;
using porganizer.Database.Common;
using porganizer.Database.Enums;

namespace porganizer.Database;

public class Indexer : BaseEntity
{
    public Guid Id { get; set; }

    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    public ParsingType ParsingType { get; set; }

    public bool IsEnabled { get; set; }

    [MaxLength(2000)]
    public string ApiKey { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ApiPath { get; set; } = string.Empty;

    /// <summary>
    /// How far back the one-time incremental backfill should fetch for this indexer, measured in days.
    /// </summary>
    public int BackfillDays { get; set; } = 30;

    /// <summary>
    /// Set when this indexer's backfill first starts. Remains fixed for the duration of the run.
    /// </summary>
    public DateTime? BackfillStartedAtUtc { get; set; }

    /// <summary>
    /// Fixed cutoff for the current/last backfill run. Rows older than this are ignored.
    /// </summary>
    public DateTime? BackfillCutoffUtc { get; set; }

    /// <summary>
    /// Set when this indexer's backfill has completed. Non-null means it must not auto-run again.
    /// </summary>
    public DateTime? BackfillCompletedAtUtc { get; set; }

    /// <summary>
    /// Set at the end of each backfill step, whether scheduled or manual.
    /// </summary>
    public DateTime? BackfillLastRunAtUtc { get; set; }

    /// <summary>
    /// Next Newznab result offset to fetch during backfill.
    /// </summary>
    public int? BackfillCurrentOffset { get; set; }
}
