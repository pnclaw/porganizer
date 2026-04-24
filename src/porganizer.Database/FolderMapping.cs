using System.ComponentModel.DataAnnotations;
using porganizer.Database.Common;

namespace porganizer.Database;

public class FolderMapping : BaseEntity
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(2000)]
    public string OriginalFolder { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string MappedToFolder { get; set; } = string.Empty;
}
