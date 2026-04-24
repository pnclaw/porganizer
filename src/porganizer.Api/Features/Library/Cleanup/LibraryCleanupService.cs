using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.Library.Cleanup;

public interface ILibraryCleanupService
{
    Task<CleanupPreviewResult> GetEligibleFilesAsync(CancellationToken ct);
    Task<CleanupDeleteResult> DeleteEligibleFilesAsync(CancellationToken ct);
}

public class LibraryCleanupService(
    AppDbContext db,
    IOptions<PreviewOptions> previewOptions,
    IOptions<ThumbnailOptions> thumbnailOptions,
    ILogger<LibraryCleanupService> logger) : ILibraryCleanupService
{
    public async Task<CleanupPreviewResult> GetEligibleFilesAsync(CancellationToken ct)
    {
        var candidates = await QueryFullyUploadedFilesAsync(ct);

        var items = candidates
            .Select(f => BuildItem(f))
            .Where(i => i.VideoFileExists || i.PreviewDirExists || i.ThumbnailDirExists)
            .ToList();

        return new CleanupPreviewResult
        {
            TotalCount = items.Count,
            TotalBytes = items.Where(i => i.VideoFileExists).Sum(i => i.FileSize),
            Items = items,
        };
    }

    public async Task<CleanupDeleteResult> DeleteEligibleFilesAsync(CancellationToken ct)
    {
        var candidates = await QueryFullyUploadedFilesAsync(ct);

        var deletedCount = 0;
        var freedBytes = 0L;

        foreach (var file in candidates)
        {
            var videoPath    = Path.Combine(file.Folder.Path, file.RelativePath);
            var previewDir   = Path.Combine(previewOptions.Value.CachePath, file.Id.ToString());
            var thumbnailDir = Path.Combine(thumbnailOptions.Value.CachePath, file.Id.ToString());

            var videoExists    = File.Exists(videoPath);
            var previewExists  = Directory.Exists(previewDir);
            var thumbnailExists = Directory.Exists(thumbnailDir);

            if (!videoExists && !previewExists && !thumbnailExists)
                continue;

            if (videoExists)
            {
                try
                {
                    File.Delete(videoPath);
                    freedBytes += file.FileSize;
                    logger.LogInformation("LibraryCleanup: deleted video file {Path}", videoPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "LibraryCleanup: failed to delete video file {Path}", videoPath);
                }
            }

            if (previewExists)
            {
                try
                {
                    Directory.Delete(previewDir, recursive: true);
                    logger.LogInformation("LibraryCleanup: deleted preview directory {Path}", previewDir);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "LibraryCleanup: failed to delete preview directory {Path}", previewDir);
                }
            }

            if (thumbnailExists)
            {
                try
                {
                    Directory.Delete(thumbnailDir, recursive: true);
                    logger.LogInformation("LibraryCleanup: deleted thumbnail directory {Path}", thumbnailDir);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "LibraryCleanup: failed to delete thumbnail directory {Path}", thumbnailDir);
                }
            }

            deletedCount++;
        }

        return new CleanupDeleteResult
        {
            DeletedCount = deletedCount,
            FreedBytes = freedBytes,
        };
    }

    private async Task<List<LibraryFile>> QueryFullyUploadedFilesAsync(CancellationToken ct) =>
        await db.LibraryFiles
            .Include(f => f.Folder)
            .Where(f =>
                (
                    f.PreviewImageCount != null &&
                    db.VideoUserImageUploads.Count(u => u.LibraryFileId == f.Id && u.PreviewImageType == "SpriteSheet") == 1 &&
                    db.VideoUserImageUploads.Count(u => u.LibraryFileId == f.Id && u.PreviewImageType == "Single") == f.PreviewImageCount
                ) ||
                f.VideoUserImageUploadCompletionReason == VideoUserImageUploadCompletionReason.SkippedPrdbAlreadyHasImages)
            .ToListAsync(ct);

    private CleanupFileItem BuildItem(LibraryFile file)
    {
        var videoPath    = Path.Combine(file.Folder.Path, file.RelativePath);
        var previewDir   = Path.Combine(previewOptions.Value.CachePath, file.Id.ToString());
        var thumbnailDir = Path.Combine(thumbnailOptions.Value.CachePath, file.Id.ToString());

        return new CleanupFileItem
        {
            LibraryFileId    = file.Id,
            RelativePath     = file.RelativePath,
            FolderPath       = file.Folder.Path,
            FileSize         = file.FileSize,
            VideoFileExists  = File.Exists(videoPath),
            PreviewDirExists = Directory.Exists(previewDir),
            ThumbnailDirExists = Directory.Exists(thumbnailDir),
        };
    }
}

public class CleanupPreviewResult
{
    public int TotalCount { get; set; }
    public long TotalBytes { get; set; }
    public List<CleanupFileItem> Items { get; set; } = [];
}

public class CleanupFileItem
{
    public Guid LibraryFileId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public bool VideoFileExists { get; set; }
    public bool PreviewDirExists { get; set; }
    public bool ThumbnailDirExists { get; set; }
}

public class CleanupDeleteResult
{
    public int DeletedCount { get; set; }
    public long FreedBytes { get; set; }
}
