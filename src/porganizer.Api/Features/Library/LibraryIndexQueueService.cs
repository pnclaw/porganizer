using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Library;

public class LibraryIndexQueueService(AppDbContext db, ILogger<LibraryIndexQueueService> logger)
{
    public async Task EnqueueFolderAsync(Guid folderId, CancellationToken ct)
    {
        var folderExists = await db.LibraryFolders.AnyAsync(f => f.Id == folderId, ct);
        if (!folderExists)
        {
            logger.LogDebug(
                "LibraryIndexQueueService: folder {FolderId} not found, skipping queue request",
                folderId);
            return;
        }

        await UpsertRequestsAsync([folderId], ct);
    }

    public async Task EnqueueForPathsAsync(IEnumerable<string> paths, CancellationToken ct)
    {
        var normalizedPaths = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizePath)
            .Distinct(PathComparer)
            .ToList();

        if (normalizedPaths.Count == 0)
            return;

        var folders = await db.LibraryFolders
            .Select(f => new { f.Id, f.Path })
            .ToListAsync(ct);

        var matchingFolderIds = folders
            .Where(folder =>
            {
                var folderPath = NormalizePath(folder.Path);
                return normalizedPaths.Any(path => IsSameOrChildPath(path, folderPath));
            })
            .Select(folder => folder.Id)
            .Distinct()
            .ToList();

        if (matchingFolderIds.Count == 0)
        {
            logger.LogDebug(
                "LibraryIndexQueueService: no library folders matched queued paths [{Paths}]",
                string.Join(", ", normalizedPaths));
            return;
        }

        await UpsertRequestsAsync(matchingFolderIds, ct);
    }

    private async Task UpsertRequestsAsync(IEnumerable<Guid> folderIds, CancellationToken ct)
    {
        var distinctFolderIds = folderIds.Distinct().ToList();
        if (distinctFolderIds.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var existing = await db.LibraryIndexRequests
            .Where(r => distinctFolderIds.Contains(r.LibraryFolderId))
            .ToDictionaryAsync(r => r.LibraryFolderId, ct);

        foreach (var folderId in distinctFolderIds)
        {
            if (existing.TryGetValue(folderId, out var request))
            {
                request.RequestedAtUtc = now;
                request.UpdatedAtUtc = now;
            }
            else
            {
                db.LibraryIndexRequests.Add(new LibraryIndexRequest
                {
                    LibraryFolderId = folderId,
                    RequestedAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsSameOrChildPath(string candidatePath, string parentPath)
    {
        if (string.Equals(candidatePath, parentPath, PathComparison))
            return true;

        var parentWithSeparator = parentPath + Path.DirectorySeparatorChar;
        if (candidatePath.StartsWith(parentWithSeparator, PathComparison))
            return true;

        if (Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar)
            return false;

        var altParentWithSeparator = parentPath + Path.AltDirectorySeparatorChar;
        return candidatePath.StartsWith(altParentWithSeparator, PathComparison);
    }
}
