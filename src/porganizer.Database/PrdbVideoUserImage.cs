using System.ComponentModel.DataAnnotations;

namespace porganizer.Database;

public class PrdbVideoUserImage
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }

    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    [MaxLength(20)]
    public string PreviewImageType { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    [MaxLength(50)]
    public string ModerationVisibility { get; set; } = string.Empty;

    public int? SpriteTileCount { get; set; }
    public int? SpriteTileWidth { get; set; }
    public int? SpriteTileHeight { get; set; }
    public int? SpriteColumns { get; set; }
    public int? SpriteRows { get; set; }

    public DateTime PrdbUpdatedAtUtc { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}
