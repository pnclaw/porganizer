using System.ComponentModel.DataAnnotations;

namespace porganizer.Database;

public class PrdbWantedVideo
{
    public Guid VideoId { get; set; }

    public bool IsFulfilled { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public int? FulfilledInQuality { get; set; }

    /// <summary>
    /// When true, the fulfillment service will queue downloads for all quality variants
    /// (720p, 1080p, 2160p) instead of picking a single best match.
    /// Set automatically when a video is auto-added while AutoAddAllNewVideosFulfillAllQualities is enabled.
    /// </summary>
    public bool FulfillAllQualities { get; set; }

    [MaxLength(500)]
    public string? FulfillmentExternalId { get; set; }

    public int? FulfillmentByApp { get; set; }

    public DateTime PrdbCreatedAtUtc { get; set; }
    public DateTime PrdbUpdatedAtUtc { get; set; }
    public DateTime SyncedAtUtc { get; set; }

    public PrdbVideo? Video { get; set; }
}
