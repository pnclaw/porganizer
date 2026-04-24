namespace porganizer.Database;

public class LibraryIndexRequest
{
    public Guid LibraryFolderId { get; set; }
    public LibraryFolder Folder { get; set; } = null!;

    public DateTime RequestedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
