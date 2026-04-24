using System.ComponentModel.DataAnnotations;
using porganizer.Database.Enums;

namespace porganizer.Database;

public class LibraryFile
{
    public Guid Id { get; set; }

    public Guid LibraryFolderId { get; set; }
    public LibraryFolder Folder { get; set; } = null!;

    [Required]
    [MaxLength(2000)]
    public string RelativePath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    [MaxLength(16)]
    public string? OsHash { get; set; }

    [MaxLength(16)]
    public string? PHash { get; set; }

    public Guid? VideoId { get; set; }
    public PrdbVideo? Video { get; set; }

    public DateTime LastSeenAtUtc { get; set; }
    public DateTime? HashComputedAtUtc { get; set; }

    public DateTime? SpriteSheetGeneratedAtUtc { get; set; }
    public int? SpriteSheetTileCount { get; set; }

    public DateTime? PreviewImagesGeneratedAtUtc { get; set; }
    public int? PreviewImageCount { get; set; }

    public DateTime? VideoUserImageUploadCompletedAtUtc { get; set; }
    public VideoUserImageUploadCompletionReason? VideoUserImageUploadCompletionReason { get; set; }
    public int? VideoUserImageUploadRemoteImageCount { get; set; }
}
