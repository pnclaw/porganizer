using porganizer.Database.Enums;

namespace porganizer.Database;

// Immutable log record — no BaseEntity (no UpdatedAt/CreatedBy needed)
public class IndexerApiRequest
{
    public Guid Id { get; set; }
    public Guid IndexerId { get; set; }
    public Indexer Indexer { get; set; } = null!;
    public IndexerRequestType RequestType { get; set; }
    public DateTime OccurredAt { get; set; }
    public bool Success { get; set; }

    /// <summary>HTTP status code returned by the indexer. Null for Grab events (request is made by the download client).</summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>Round-trip time in milliseconds. For Grab events this is the time to hand off to the download client.</summary>
    public int? ResponseTimeMs { get; set; }
}
