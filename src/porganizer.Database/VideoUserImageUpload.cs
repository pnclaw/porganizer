using System.ComponentModel.DataAnnotations;
using porganizer.Database.Common;

namespace porganizer.Database;

public class VideoUserImageUpload : BaseEntity
{
    public Guid Id { get; set; }

    public Guid LibraryFileId { get; set; }
    public LibraryFile LibraryFile { get; set; } = null!;

    public Guid? PrdbVideoId { get; set; }

    public Guid PrdbVideoUserImageId { get; set; }

    [MaxLength(20)]
    public string PreviewImageType { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public DateTime UploadedAtUtc { get; set; }
}
