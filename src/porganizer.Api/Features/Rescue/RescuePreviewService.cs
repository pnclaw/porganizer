using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.Shared;
using porganizer.Database;

namespace porganizer.Api.Features.Rescue;

public interface IRescuePreviewService
{
    Task<RescuePreviewResponse> PreviewAsync(string folder);
}

public class RescuePreviewService(AppDbContext db) : IRescuePreviewService
{
    public async Task<RescuePreviewResponse> PreviewAsync(string folder)
    {
        var candidates = ScanSubdirectories(folder);
        if (candidates.Count == 0)
            return new RescuePreviewResponse();

        var settings = await db.GetSettingsAsync();
        var normalizedNames = candidates.Select(c => c.NormalizedName).ToHashSet();

        var indexerRows = await db.IndexerRows
            .Where(r => normalizedNames.Contains(
                r.Title.Replace(".", " ").Replace("-", " ").Replace("_", " ").ToLower().Trim()))
            .Select(r => new { r.Id, r.Title })
            .ToListAsync();

        var rowsByNorm = indexerRows
            .GroupBy(r => Normalize(r.Title))
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var rowIds = indexerRows.Select(r => r.Id).ToList();

        var matchesByRowId = await db.IndexerRowMatches
            .Include(m => m.Video).ThenInclude(v => v.Site)
            .Where(m => rowIds.Contains(m.IndexerRowId))
            .ToDictionaryAsync(m => m.IndexerRowId);

        var items = candidates.Select(candidate =>
        {
            IndexerRowMatch? match = null;
            if (rowsByNorm.TryGetValue(candidate.NormalizedName, out var ids))
            {
                match = ids
                    .Select(id => matchesByRowId.TryGetValue(id, out var m) ? m : null)
                    .FirstOrDefault(m => m?.Video?.Site != null);
            }

            var siteName  = match != null ? SanitizeFileName(match.Video!.Site!.Title) : null;
            var targetRoot = settings.CompletedDownloadsTargetFolder;

            return new RescuePreviewItem
            {
                SourcePath        = candidate.SourcePath,
                Name              = candidate.Name,
                IsMatched         = match != null,
                VideoTitle        = match?.Video?.Title,
                SiteTitle         = match?.Video?.Site?.Title,
                DestinationFolder = siteName != null && !string.IsNullOrWhiteSpace(targetRoot)
                    ? Path.Combine(targetRoot, siteName)
                    : null,
                VideoFileCount    = candidate.VideoFileCount,
            };
        }).ToList();

        return new RescuePreviewResponse { Items = items };
    }

    internal static List<RescueScanItem> ScanSubdirectories(string folder) =>
        Directory.GetDirectories(folder)
            .Select(dir =>
            {
                var name  = Path.GetFileName(dir)!;
                var count = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Count(f => VideoExtensions.All.Contains(Path.GetExtension(f)));
                return new RescueScanItem(dir, name, Normalize(name), count);
            })
            .ToList();

    internal static string Normalize(string s) =>
        s.Replace('.', ' ').Replace('-', ' ').Replace('_', ' ')
         .ToLowerInvariant()
         .Trim();

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Split(Path.GetInvalidFileNameChars()));
}

internal sealed record RescueScanItem(string SourcePath, string Name, string NormalizedName, int VideoFileCount);
