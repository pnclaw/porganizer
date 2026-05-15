using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.DownloadClients;
using porganizer.Api.Features.Shared;
using porganizer.Api.Features.WantedFulfillment;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.Rescue;

public interface IRescueExecuteService
{
    Task<RescueExecuteResponse> ExecuteAsync(string folder, CancellationToken ct);
}

public class RescueExecuteService(
    AppDbContext db,
    DownloadFileMoveService downloadFileMoveService) : IRescueExecuteService
{
    public async Task<RescueExecuteResponse> ExecuteAsync(string folder, CancellationToken ct)
    {
        var candidates = RescuePreviewService.ScanSubdirectories(folder);
        var response   = new RescueExecuteResponse();

        if (candidates.Count == 0)
            return response;

        var settings        = await db.GetSettingsAsync();
        var downloadClient  = await db.DownloadClients.FirstOrDefaultAsync(ct);
        var normalizedNames = candidates.Select(c => c.NormalizedName).ToHashSet();

        var indexerRows = await db.IndexerRows
            .Where(r => normalizedNames.Contains(
                r.Title.Replace(".", " ").Replace("-", " ").Replace("_", " ").ToLower().Trim()))
            .Select(r => new { r.Id, r.Title, r.NzbUrl })
            .ToListAsync(ct);

        var rowsByNorm = indexerRows
            .GroupBy(r => RescuePreviewService.Normalize(r.Title))
            .ToDictionary(g => g.Key, g => g.ToList());

        var rowIds = indexerRows.Select(r => r.Id).ToList();

        var matchesByRowId = await db.IndexerRowMatches
            .Include(m => m.Video).ThenInclude(v => v.Site)
            .Where(m => rowIds.Contains(m.IndexerRowId))
            .ToDictionaryAsync(m => m.IndexerRowId, ct);

        foreach (var candidate in candidates)
        {
            var item = new RescueExecuteItem { SourcePath = candidate.SourcePath, Name = candidate.Name };
            response.Items.Add(item);

            if (!rowsByNorm.TryGetValue(candidate.NormalizedName, out var rows))
            {
                item.Log.Add(new RescueLogEntry { Level = "warning", Message = "No indexer row matched this folder name — skipping." });
                continue;
            }

            var matched = rows
                .Select(r => (Row: r, Match: matchesByRowId.TryGetValue(r.Id, out var m) ? m : null))
                .FirstOrDefault(x => x.Match?.Video?.Site != null);

            if (matched.Match == null)
            {
                item.Log.Add(new RescueLogEntry { Level = "warning", Message = "Indexer row found but no prdb.net video match — skipping." });
                continue;
            }

            item.IsMatched = true;

            if (downloadClient == null)
            {
                item.Log.Add(new RescueLogEntry { Level = "error", Message = "No download client configured — cannot record this rescue." });
                continue;
            }

            var now = DateTime.UtcNow;

            var log = await db.DownloadLogs
                .Include(l => l.Files)
                .Where(l => l.IndexerRowId == matched.Row.Id && l.FilesMovedAtUtc == null)
                .FirstOrDefaultAsync(ct);

            if (log != null)
            {
                log.StoragePath = candidate.SourcePath;
                log.Status      = DownloadStatus.Completed;
                log.UpdatedAt   = now;
                item.Log.Add(new RescueLogEntry { Level = "info", Message = $"Reusing existing download log {log.Id}." });
            }
            else
            {
                log = new DownloadLog
                {
                    Id               = Guid.NewGuid(),
                    IndexerRowId     = matched.Row.Id,
                    DownloadClientId = downloadClient.Id,
                    NzbName          = matched.Row.Title,
                    NzbUrl           = matched.Row.NzbUrl,
                    Status           = DownloadStatus.Completed,
                    StoragePath      = candidate.SourcePath,
                    CompletedAt      = now,
                    CreatedAt        = now,
                    UpdatedAt        = now,
                };
                db.DownloadLogs.Add(log);
            }

            var existingFileNames = log.Files.Select(f => f.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var videoFiles = Directory.GetFiles(candidate.SourcePath, "*", SearchOption.AllDirectories)
                .Where(f => VideoExtensions.All.Contains(Path.GetExtension(f)))
                .Order()
                .ToList();

            foreach (var videoFile in videoFiles)
            {
                var relative = Path.GetRelativePath(candidate.SourcePath, videoFile);
                if (existingFileNames.Contains(relative)) continue;

                var fi = new FileInfo(videoFile);
                db.DownloadLogFiles.Add(new DownloadLogFile
                {
                    Id               = Guid.NewGuid(),
                    DownloadLogId    = log.Id,
                    FileName         = relative,
                    OriginalFileName = relative,
                    FileSize         = fi.Length,
                    OsHash           = OsHash.Compute(videoFile),
                    CreatedAt        = now,
                    UpdatedAt        = now,
                });
            }

            await db.SaveChangesAsync(ct);

            var moveEntries = await downloadFileMoveService.MoveAsync([log.Id], settings, ct);
            foreach (var entry in moveEntries)
            {
                item.Log.Add(new RescueLogEntry
                {
                    Level   = entry.Level switch { MoveLogLevel.Info => "info", MoveLogLevel.Warning => "warning", _ => "error" },
                    Message = entry.Message,
                });
            }

            if (moveEntries.Any(e => e.Level == MoveLogLevel.Info && e.Message.StartsWith("Moved:")))
                await FulfillWantedVideoAsync(matched.Match.PrdbVideoId, log.Id, matched.Row.Title, ct);
        }

        return response;
    }

    private async Task FulfillWantedVideoAsync(Guid videoId, Guid logId, string rowTitle, CancellationToken ct)
    {
        var wanted = await db.PrdbWantedVideos
            .Where(w => w.VideoId == videoId && !w.IsFulfilled)
            .FirstOrDefaultAsync(ct);

        if (wanted == null) return;

        wanted.IsFulfilled           = true;
        wanted.FulfilledAtUtc        = DateTime.UtcNow;
        wanted.FulfillmentExternalId = logId.ToString();
        wanted.FulfilledInQuality    = (int?)WantedVideoFulfillmentService.ParseQuality(rowTitle);
        await db.SaveChangesAsync(ct);
    }
}
