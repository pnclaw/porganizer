using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.Library;
using porganizer.Api.Features.WantedFulfillment;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.DownloadClients;

public class DownloadFileMoveService(
    AppDbContext db,
    LibraryIndexQueueService libraryIndexQueueService,
    ILogger<DownloadFileMoveService> logger)
{
    public async Task<IReadOnlyList<MoveLogEntry>> MoveAsync(
        IReadOnlyCollection<Guid> downloadLogIds,
        AppSettings settings,
        CancellationToken ct)
    {
        var entries = new List<MoveLogEntry>();

        if (!settings.OrganizeCompletedBySite ||
            string.IsNullOrWhiteSpace(settings.CompletedDownloadsTargetFolder))
        {
            entries.Add(new(MoveLogLevel.Warning, "Move is not configured: enable 'Organize by site' and set a target folder in Settings."));
            return entries;
        }

        if (downloadLogIds.Count == 0)
            return entries;

        var logs = await db.DownloadLogs
            .Include(l => l.Files)
            .Include(l => l.IndexerRow)
            .Where(l => downloadLogIds.Contains(l.Id) && l.Files.Count > 0)
            .ToListAsync(ct);

        if (logs.Count == 0)
        {
            entries.Add(new(MoveLogLevel.Warning, "No files are recorded for this download — nothing to move."));
            return entries;
        }

        var indexerRowIds = logs.Select(l => l.IndexerRowId).ToList();

        var matches = await db.Set<IndexerRowMatch>()
            .Include(m => m.Video)
                .ThenInclude(v => v.Site)
            .Include(m => m.IndexerRow)
            .Where(m => indexerRowIds.Contains(m.IndexerRowId))
            .ToListAsync(ct);

        var folderMappings = await db.FolderMappings.ToListAsync(ct);

        var anyChanges = false;
        var now = DateTime.UtcNow;
        var queuedLibraryPaths = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var log in logs)
        {
            var match = matches.FirstOrDefault(m => m.IndexerRowId == log.IndexerRowId);
            if (match?.Video?.Site == null)
            {
                logger.LogDebug(
                    "DownloadFileMoveService: no prdb.net video match for log {LogId}, skipping",
                    log.Id);
                entries.Add(new(MoveLogLevel.Warning, "No prdb.net video match found — cannot determine the destination site folder."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(log.StoragePath))
            {
                logger.LogDebug(
                    "DownloadFileMoveService: no StoragePath for log {LogId}, skipping",
                    log.Id);
                entries.Add(new(MoveLogLevel.Warning, "No source folder is recorded for this download."));
                continue;
            }

            var targetRoot = settings.CompletedDownloadsTargetFolder;

            if (!Directory.Exists(targetRoot))
            {
                logger.LogWarning(
                    "DownloadFileMoveService: target folder '{Folder}' does not exist, skipping log {LogId}",
                    targetRoot, log.Id);
                entries.Add(new(MoveLogLevel.Warning, $"Target folder does not exist: {targetRoot}"));
                continue;
            }

            var siteName    = SanitizeFileName(match.Video.Site.Title);
            var siteFolder  = Path.Combine(targetRoot, siteName);
            Directory.CreateDirectory(siteFolder);

            var sourceFolder = ApplyFolderMapping(log.StoragePath, folderMappings);
            logger.LogInformation(
                "DownloadFileMoveService: log {LogId} storagePath='{StoragePath}' sourceFolder='{SourceFolder}'",
                log.Id, log.StoragePath, sourceFolder);

            if (!Directory.Exists(sourceFolder))
            {
                logger.LogDebug(
                    "DownloadFileMoveService: source folder '{Folder}' not found for log {LogId}, skipping",
                    sourceFolder, log.Id);
                entries.Add(new(MoveLogLevel.Warning, $"Source folder not found: {sourceFolder}"));
                continue;
            }

            var qualityLabel = QualityLabel(WantedVideoFulfillmentService.ParseQuality(log.IndexerRow.Title));
            var video        = match.Video;
            var files        = log.Files.DistinctBy(f => f.Id).ToList();
            var movedCount   = 0;

            logger.LogInformation(
                "DownloadFileMoveService: log {LogId} has {FileCount} file(s): [{FileNames}]",
                log.Id, files.Count, string.Join(", ", files.Select(f => f.FileName)));

            for (var i = 0; i < files.Count; i++)
            {
                var file           = files[i];
                var sourceFilePath = Path.Combine(sourceFolder, file.FileName);

                if (!File.Exists(sourceFilePath))
                {
                    // Log sibling files so we can see what names actually exist in the folder
                    var siblings = Directory.Exists(sourceFolder)
                        ? Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories)
                        : [];
                    logger.LogWarning(
                        "DownloadFileMoveService: source file '{Path}' not found for log {LogId}. " +
                        "Folder contains {Count} file(s): [{Siblings}]",
                        sourceFilePath, log.Id, siblings.Length,
                        string.Join(", ", siblings.Select(Path.GetFileName)));
                    entries.Add(new(MoveLogLevel.Warning, $"File not found: {sourceFilePath}"));
                    continue;
                }

                var ext      = Path.GetExtension(file.FileName);
                var index    = files.Count > 1 ? i + 1 : (int?)null;

                logger.LogInformation(
                    "DownloadFileMoveService: file [{Index}/{Total}] '{FileName}', rename={Rename}, index={FileIndex}",
                    i + 1, files.Count, file.FileName, settings.RenameCompletedFiles, index);

                var rawFileName = Path.GetFileName(file.FileName).Trim('\'');
                logger.LogInformation(
                    "DownloadFileMoveService: file db FileName='{DbFileName}', sourceFilePath='{SourceFilePath}', rawFileName='{RawFileName}'",
                    file.FileName, sourceFilePath, rawFileName);

                var destName = settings.RenameCompletedFiles
                    ? BuildRenamedFileName(siteName, video.Title, video.ReleaseDate, qualityLabel, index, ext)
                    : rawFileName;
                logger.LogInformation(
                    "DownloadFileMoveService: destName='{DestName}' destFolder='{DestFolder}'",
                    destName, siteFolder);

                var destPath = UniqueDestPath(siteFolder, destName);

                try
                {
                    File.Move(sourceFilePath, destPath);
                    file.FileName  = Path.GetFileName(destPath);
                    file.UpdatedAt = now;
                    movedCount++;
                    logger.LogInformation(
                        "DownloadFileMoveService: moved '{Source}' → '{Dest}'",
                        sourceFilePath, destPath);
                    entries.Add(new(MoveLogLevel.Info, $"Moved: {sourceFilePath} → {destPath}"));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "DownloadFileMoveService: failed to move '{Source}' → '{Dest}'",
                        sourceFilePath, destPath);
                    entries.Add(new(MoveLogLevel.Error, $"Failed to move {sourceFilePath}: {ex.Message}"));
                }
            }

            if (movedCount == 0)
            {
                entries.Add(new(MoveLogLevel.Warning, "No files were moved."));
                continue;
            }

            log.StoragePath    = siteFolder;
            log.FilesMovedAtUtc = now;
            log.UpdatedAt      = now;
            anyChanges         = true;
            queuedLibraryPaths.Add(siteFolder);

            TryDeleteIfEmpty(sourceFolder, log.Id, entries);
        }

        if (anyChanges)
        {
            await db.SaveChangesAsync(ct);
            await libraryIndexQueueService.EnqueueForPathsAsync(queuedLibraryPaths, ct);
        }

        return entries;
    }

    private static string BuildRenamedFileName(
        string siteName,
        string title,
        DateOnly? releaseDate,
        string? qualityLabel,
        int? index,
        string ext)
    {
        var parts = new List<string> { siteName, title };

        if (releaseDate.HasValue)
            parts.Add(releaseDate.Value.ToString("yyyy-MM-dd"));

        if (qualityLabel != null)
            parts.Add(qualityLabel);

        var baseName = string.Join(" - ", parts);

        if (index.HasValue)
            baseName += $" - {index.Value}";

        return SanitizeFileName(baseName) + ext;
    }

    private static string? QualityLabel(VideoQuality? quality) => quality switch
    {
        VideoQuality.P720  => "720p",
        VideoQuality.P1080 => "1080p",
        VideoQuality.P2160 => "2160p",
        _                  => null,
    };

    private static string ApplyFolderMapping(string path, List<FolderMapping> mappings)
    {
        foreach (var mapping in mappings)
        {
            if (path.StartsWith(mapping.OriginalFolder, StringComparison.OrdinalIgnoreCase))
                return mapping.MappedToFolder + path[mapping.OriginalFolder.Length..];
        }

        return path;
    }

    private static string UniqueDestPath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate))
            return candidate;

        var ext  = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);

        for (var n = 1; n < 1000; n++)
        {
            candidate = Path.Combine(folder, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return candidate;
    }

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Split(Path.GetInvalidFileNameChars()));

    private void TryDeleteIfEmpty(string folder, Guid logId, List<MoveLogEntry> entries)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
                logger.LogDebug(
                    "DownloadFileMoveService: deleted empty source folder '{Folder}' for log {LogId}",
                    folder, logId);
                entries.Add(new(MoveLogLevel.Info, $"Removed empty source folder: {folder}"));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "DownloadFileMoveService: failed to delete source folder '{Folder}' for log {LogId}",
                folder, logId);
            entries.Add(new(MoveLogLevel.Warning, $"Could not remove source folder '{folder}': {ex.Message}"));
        }
    }
}
