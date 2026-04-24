using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.DownloadClients;

public class DownloadLibraryFolderService(
    AppDbContext db,
    ILogger<DownloadLibraryFolderService> logger)
{
    /// <summary>
    /// Ensures a LibraryFolder exists for the configured DownloadLibraryPath.
    /// If the path is not set or already has a matching folder, this is a no-op.
    /// </summary>
    public async Task SyncAsync(CancellationToken ct = default)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.DownloadLibraryPath))
            return;

        var path = settings.DownloadLibraryPath.Trim();

        var exists = await db.LibraryFolders
            .AnyAsync(f => f.Path == path, ct);

        if (exists)
            return;

        db.LibraryFolders.Add(new LibraryFolder
        {
            Id = Guid.NewGuid(),
            Path = path,
            Label = "Downloads",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "DownloadLibraryFolderService: registered '{Path}' as a library folder",
            path);
    }
}
