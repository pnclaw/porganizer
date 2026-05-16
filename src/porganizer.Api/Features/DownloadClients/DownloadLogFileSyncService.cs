using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.Shared;
using porganizer.Database;

namespace porganizer.Api.Features.DownloadClients;

public class DownloadLogFileSyncService(AppDbContext db, ILogger<DownloadLogFileSyncService> logger)
{
    public async Task<Dictionary<Guid, DownloadLogFileSyncResult>> SyncAsync(
        IReadOnlyCollection<Guid> downloadLogIds,
        bool deleteNonVideoFiles,
        CancellationToken ct)
    {
        if (downloadLogIds.Count == 0)
            return [];

        var logs = await db.DownloadLogs
            .Include(l => l.Files)
            .Where(l => downloadLogIds.Contains(l.Id))
            .ToListAsync(ct);

        if (logs.Count == 0)
            return [];

        var folderMappings = await db.FolderMappings.ToListAsync(ct);
        var results = new Dictionary<Guid, DownloadLogFileSyncResult>();
        var anyChanges = false;

        foreach (var log in logs)
        {
            var result = SyncLogFiles(log, folderMappings, deleteNonVideoFiles);
            results[log.Id] = result;
            anyChanges |= result.HasChanges;
        }

        if (anyChanges)
            await db.SaveChangesAsync(ct);

        return results;
    }

    private DownloadLogFileSyncResult SyncLogFiles(DownloadLog log, List<FolderMapping> folderMappings, bool deleteNonVideoFiles)
    {
        // After files are moved, StoragePath points to the shared site folder which is shared by
        // multiple downloads. Re-scanning it would assign sibling files to the wrong log.
        // The DownloadLogFile records are already correct from the initial scan, so return them as-is.
        if (log.FilesMovedAtUtc != null)
        {
            return new DownloadLogFileSyncResult(
                directoryExists: true,
                hasFiles: log.Files.Count > 0,
                addedOrUpdatedFileIds: log.Files.Select(f => f.Id).ToList(),
                removedFiles: []);
        }

        if (string.IsNullOrWhiteSpace(log.StoragePath))
            return DownloadLogFileSyncResult.NoDirectory();

        var localPath = log.StoragePath;
        foreach (var mapping in folderMappings)
        {
            if (localPath.StartsWith(mapping.OriginalFolder, StringComparison.OrdinalIgnoreCase))
            {
                localPath = mapping.MappedToFolder + localPath[mapping.OriginalFolder.Length..];
                break;
            }
        }

        string scanRoot;
        string[] allPaths;
        var storagePathChanged = false;

        if (Directory.Exists(localPath))
        {
            scanRoot = localPath;
            allPaths = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);
        }
        else if (File.Exists(localPath))
        {
            var localParent = Path.GetDirectoryName(localPath);
            var logParent = GetParentPath(log.StoragePath);

            if (string.IsNullOrWhiteSpace(localParent) || string.IsNullOrWhiteSpace(logParent))
                return DownloadLogFileSyncResult.NoDirectory();

            scanRoot = localParent;
            allPaths = [localPath];
            if (!string.Equals(log.StoragePath, logParent, StringComparison.Ordinal))
            {
                log.StoragePath = logParent;
                storagePathChanged = true;
            }
        }
        else
        {
            logger.LogDebug("DownloadLogFileSyncService: directory not found at '{Path}' for log {LogId}", localPath, log.Id);
            return DownloadLogFileSyncResult.NoDirectory();
        }

        if (deleteNonVideoFiles)
        {
            foreach (var nonVideo in allPaths.Where(p => !VideoExtensions.All.Contains(Path.GetExtension(p))))
            {
                try
                {
                    File.Delete(nonVideo);
                    logger.LogDebug("DownloadLogFileSyncService: deleted non-video file '{Path}'", nonVideo);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "DownloadLogFileSyncService: failed to delete non-video file '{Path}'", nonVideo);
                }
            }
        }

        var fullPaths = allPaths
            .Where(p => VideoExtensions.All.Contains(Path.GetExtension(p)))
            .Order()
            .ToList();

        if (fullPaths.Count == 0)
        {
            if (log.Files.Count == 0)
                return new DownloadLogFileSyncResult(true, false, [], []);

            var removed = log.Files
                .Select(f => new RemovedDownloadLogFileSnapshot(f.Id, f.FileName, f.PrdbDownloadedFromIndexerFilenameId))
                .ToList();

            db.DownloadLogFiles.RemoveRange(log.Files);
            log.UpdatedAt = DateTime.UtcNow;

            return new DownloadLogFileSyncResult(true, false, [], removed)
            {
                HasChanges = true
            };
        }

        var scannedByName = new Dictionary<string, ScannedDownloadLogFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var fullPath in fullPaths)
        {
            var fileInfo = new FileInfo(fullPath);
            var relativePath = Path.GetRelativePath(scanRoot, fullPath);
            logger.LogInformation(
                "DownloadLogFileSyncService: scanned file log={LogId} relativePath='{RelativePath}' " +
                "hasLeadingQuote={HasLeadingQuote} hasTrailingQuote={HasTrailingQuote}",
                log.Id, relativePath,
                Path.GetFileName(relativePath).StartsWith('\''),
                Path.GetFileName(relativePath).EndsWith('\''));
            scannedByName[relativePath] = new ScannedDownloadLogFile(
                relativePath,
                fileInfo.Length,
                OsHash.Compute(fullPath),
                null);
        }

        var existingByName = log.Files.ToDictionary(f => f.FileName, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var addedOrUpdated = new List<Guid>();
        var removedSnapshots = new List<RemovedDownloadLogFileSnapshot>();
        var hasChanges = storagePathChanged;

        foreach (var scanned in scannedByName.Values)
        {
            if (existingByName.TryGetValue(scanned.FileName, out var existing))
            {
                if (existing.FileSize != scanned.FileSize ||
                    !string.Equals(existing.OsHash, scanned.OsHash, StringComparison.Ordinal) ||
                    !string.Equals(existing.PHash, scanned.PHash, StringComparison.Ordinal))
                {
                    existing.FileSize = scanned.FileSize;
                    existing.OsHash = scanned.OsHash;
                    existing.PHash = scanned.PHash;
                    existing.UpdatedAt = now;
                    hasChanges = true;
                }

                addedOrUpdated.Add(existing.Id);
                continue;
            }

            var file = new DownloadLogFile
            {
                Id = Guid.NewGuid(),
                DownloadLogId = log.Id,
                OriginalFileName = scanned.FileName,
                FileName = scanned.FileName,
                FileSize = scanned.FileSize,
                OsHash = scanned.OsHash,
                PHash = scanned.PHash,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.DownloadLogFiles.Add(file);
            log.Files.Add(file);
            addedOrUpdated.Add(file.Id);
            hasChanges = true;
        }

        foreach (var existing in log.Files.ToList())
        {
            if (scannedByName.ContainsKey(existing.FileName))
                continue;

            removedSnapshots.Add(new RemovedDownloadLogFileSnapshot(
                existing.Id,
                existing.FileName,
                existing.PrdbDownloadedFromIndexerFilenameId));
            db.DownloadLogFiles.Remove(existing);
            log.Files.Remove(existing);
            hasChanges = true;
        }

        if (hasChanges)
            log.UpdatedAt = now;

        return new DownloadLogFileSyncResult(true, log.Files.Count > 0, addedOrUpdated, removedSnapshots)
        {
            HasChanges = hasChanges
        };
    }

    private static string? GetParentPath(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\');
        var index = trimmed.LastIndexOfAny(['/', '\\']);

        if (index > 0)
            return trimmed[..index];

        return Path.GetDirectoryName(trimmed);
    }
}

public class DownloadLogFileSyncResult(
    bool directoryExists,
    bool hasFiles,
    IReadOnlyList<Guid> addedOrUpdatedFileIds,
    IReadOnlyList<RemovedDownloadLogFileSnapshot> removedFiles)
{
    public bool DirectoryExists { get; } = directoryExists;
    public bool HasFiles { get; } = hasFiles;
    public IReadOnlyList<Guid> AddedOrUpdatedFileIds { get; } = addedOrUpdatedFileIds;
    public IReadOnlyList<RemovedDownloadLogFileSnapshot> RemovedFiles { get; } = removedFiles;
    public bool HasChanges { get; init; }

    public static DownloadLogFileSyncResult NoDirectory() => new(false, false, [], []);
}

public record RemovedDownloadLogFileSnapshot(Guid Id, string FileName, Guid? PrdbDownloadedFromIndexerFilenameId);

file sealed record ScannedDownloadLogFile(string FileName, long FileSize, string? OsHash, string? PHash);
