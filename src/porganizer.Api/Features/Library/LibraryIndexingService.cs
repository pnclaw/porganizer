using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.Shared;
using porganizer.Database;

namespace porganizer.Api.Features.Library;

public class LibraryIndexingService(
    AppDbContext db,
    ThumbnailQueueService thumbnailQueue,
    PreviewQueueService previewQueue,
    ILogger<LibraryIndexingService> logger)
{
    private static readonly TimeSpan ReIndexThreshold = TimeSpan.FromHours(24);

    /// <summary>
    /// Indexes all library folders that have not been indexed within the last 24 hours.
    /// Called by the background SyncWorker.
    /// </summary>
    public async Task IndexAllAsync(CancellationToken ct)
    {
        var folders = await db.LibraryFolders.ToListAsync(ct);

        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();

            if (folder.LastIndexedAtUtc.HasValue &&
                DateTime.UtcNow - folder.LastIndexedAtUtc.Value < ReIndexThreshold)
            {
                logger.LogDebug("LibraryIndexingService: skipping folder {FolderId} — indexed recently", folder.Id);
                continue;
            }

            await IndexFolderCoreAsync(folder, ct);
        }
    }

    /// <summary>
    /// Indexes a single library folder immediately, regardless of last-indexed time.
    /// Called on-demand from the controller.
    /// </summary>
    public async Task IndexFolderAsync(Guid folderId, CancellationToken ct)
    {
        var folder = await db.LibraryFolders.FindAsync([folderId], ct);
        if (folder is null)
        {
            logger.LogWarning("LibraryIndexingService: folder {FolderId} not found", folderId);
            return;
        }

        await IndexFolderCoreAsync(folder, ct);
    }

    private async Task IndexFolderCoreAsync(LibraryFolder folder, CancellationToken ct)
    {
        logger.LogInformation("LibraryIndexingService: indexing folder '{Path}'", folder.Path);

        folder.IndexingStartedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            if (!Directory.Exists(folder.Path))
            {
                logger.LogWarning("LibraryIndexingService: directory not found at '{Path}'", folder.Path);
                folder.IndexingStartedAtUtc = null;
                folder.LastIndexedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }

            var settings = await db.GetSettingsAsync(ct);
            var thumbnailMatchedOnly = settings?.ThumbnailGenerationMatchedOnly ?? false;
            var previewMatchedOnly   = settings?.PreviewImageGenerationMatchedOnly ?? false;

            var allFilePaths = Directory.GetFiles(folder.Path, "*", SearchOption.AllDirectories)
                .Where(p => VideoExtensions.All.Contains(Path.GetExtension(p)))
                .OrderBy(p => p)
                .ToList();

            // Load existing LibraryFile records for this folder
            var existingFiles = await db.LibraryFiles
                .Where(f => f.LibraryFolderId == folder.Id)
                .ToListAsync(ct);

            var duplicatePaths = existingFiles
                .GroupBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicatePaths.Count > 0)
                logger.LogWarning(
                    "LibraryIndexingService: folder '{Path}' has {Count} duplicate RelativePath(s) in the database — {Paths}",
                    folder.Path, duplicatePaths.Count, duplicatePaths);

            var existingByPath = existingFiles
                .GroupBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;
            var pendingThumbnails = new List<Guid>();
            var pendingPreviews   = new List<Guid>();

            foreach (var fullPath in allFilePaths)
            {
                ct.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(folder.Path, fullPath);
                if (!seenPaths.Add(relativePath))
                {
                    logger.LogWarning(
                        "LibraryIndexingService: skipping duplicate path '{RelativePath}' in folder '{Path}'",
                        relativePath, folder.Path);
                    continue;
                }

                var fileInfo = new FileInfo(fullPath);
                var osHash = OsHash.Compute(fullPath);

                // Match against PrdbVideoFilehash to find a linked PrdbVideo
                Guid? videoId = null;
                if (osHash is not null)
                {
                    videoId = await db.PrdbVideoFilehashes
                        .Where(h => h.OsHash == osHash)
                        .Select(h => h.VideoId)
                        .FirstOrDefaultAsync(ct);
                }

                if (existingByPath.TryGetValue(relativePath, out var existing))
                {
                    var hashChanged = existing.OsHash != osHash;
                    existing.FileSize = fileInfo.Length;
                    existing.OsHash = osHash;
                    existing.VideoId = videoId;
                    existing.LastSeenAtUtc = now;
                    existing.HashComputedAtUtc = osHash is not null ? now : existing.HashComputedAtUtc;

                    // Re-generate sprite sheet and previews if the file content changed
                    if (hashChanged)
                    {
                        existing.SpriteSheetGeneratedAtUtc = null;
                        existing.SpriteSheetTileCount = null;
                        existing.PreviewImagesGeneratedAtUtc = null;
                        existing.PreviewImageCount = null;
                        if (!thumbnailMatchedOnly || videoId != null)
                            pendingThumbnails.Add(existing.Id);
                        if (!previewMatchedOnly || videoId != null)
                            pendingPreviews.Add(existing.Id);
                    }
                    else
                    {
                        if (existing.SpriteSheetGeneratedAtUtc is null)
                            if (!thumbnailMatchedOnly || videoId != null)
                                pendingThumbnails.Add(existing.Id);
                        if (existing.PreviewImagesGeneratedAtUtc is null)
                            if (!previewMatchedOnly || videoId != null)
                                pendingPreviews.Add(existing.Id);
                    }
                }
                else
                {
                    var file = new LibraryFile
                    {
                        Id = Guid.NewGuid(),
                        LibraryFolderId = folder.Id,
                        RelativePath = relativePath,
                        FileSize = fileInfo.Length,
                        OsHash = osHash,
                        VideoId = videoId,
                        LastSeenAtUtc = now,
                        HashComputedAtUtc = osHash is not null ? now : null,
                    };
                    db.LibraryFiles.Add(file);
                    if (!thumbnailMatchedOnly || videoId != null)
                        pendingThumbnails.Add(file.Id);
                    if (!previewMatchedOnly || videoId != null)
                        pendingPreviews.Add(file.Id);
                }
            }

            // Remove files no longer present on disk
            foreach (var stale in existingFiles.Where(f => !seenPaths.Contains(f.RelativePath)))
            {
                db.LibraryFiles.Remove(stale);
            }

            await db.SaveChangesAsync(ct);

            thumbnailQueue.EnqueueMany(pendingThumbnails);
            previewQueue.EnqueueMany(pendingPreviews);

            // Recount after saves
            var fileCount = await db.LibraryFiles
                .Where(f => f.LibraryFolderId == folder.Id)
                .CountAsync(ct);

            var matchedCount = await db.LibraryFiles
                .Where(f => f.LibraryFolderId == folder.Id && f.VideoId != null)
                .CountAsync(ct);

            folder.FileCount = fileCount;
            folder.MatchedCount = matchedCount;
            folder.LastIndexedAtUtc = now;
            folder.IndexingStartedAtUtc = null;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "LibraryIndexingService: indexed '{Path}' — {FileCount} files, {MatchedCount} matched",
                folder.Path, fileCount, matchedCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "LibraryIndexingService: error indexing folder '{Path}'", folder.Path);
            folder.IndexingStartedAtUtc = null;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}
