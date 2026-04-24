using System.ComponentModel.DataAnnotations;

namespace porganizer.Database;

public class LibraryFolder
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Path { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Label { get; set; }

    public DateTime? LastIndexedAtUtc { get; set; }
    public DateTime? IndexingStartedAtUtc { get; set; }

    public int FileCount { get; set; }
    public int MatchedCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<LibraryFile> Files { get; set; } = [];
}
