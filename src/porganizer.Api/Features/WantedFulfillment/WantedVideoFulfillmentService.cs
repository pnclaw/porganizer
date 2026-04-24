using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.DownloadClients;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.WantedFulfillment;

public class WantedVideoFulfillmentService(
    AppDbContext db,
    DownloadClientSender sender,
    ILogger<WantedVideoFulfillmentService> logger)
{
    private static readonly VideoQuality[] AllQualities = [VideoQuality.P720, VideoQuality.P1080, VideoQuality.P2160];

    private sealed class Counters
    {
        public int Sent;
        public int Failed;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);
        var preferred = settings.PreferredVideoQuality;

        var client = await db.DownloadClients
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.Title)
            .FirstOrDefaultAsync(ct);

        if (client is null)
        {
            logger.LogDebug("WantedVideoFulfillmentService: no enabled download client — skipping");
            return;
        }

        var counters = new Counters();

        await RunNormalAsync(preferred, client, counters, ct);
        await RunAllQualitiesAsync(client, counters, ct);

        if (counters.Sent > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "WantedVideoFulfillmentService: {Sent} queued, {Failed} failed to send",
            counters.Sent, counters.Failed);
    }

    // Standard fulfillment: pick the single best quality match per video.
    private async Task RunNormalAsync(
        VideoQuality preferred,
        DownloadClient client,
        Counters counters,
        CancellationToken ct)
    {
        // Matches for unfulfilled wanted videos (not in all-qualities mode) where no download log
        // exists (in any status, including Failed) for any indexer row associated with the same
        // video. Failed downloads must be retried manually via the recheck endpoint to avoid
        // thrashing the download client.
        var matches = await db.IndexerRowMatches
            .Include(m => m.IndexerRow)
            .Where(m => db.PrdbWantedVideos.Any(w =>
                w.VideoId == m.PrdbVideoId && !w.IsFulfilled && !w.FulfillAllQualities))
            .Where(m => !db.IndexerRowMatches
                .Where(m2 => m2.PrdbVideoId == m.PrdbVideoId)
                .Any(m2 => db.DownloadLogs.Any(l => l.IndexerRowId == m2.IndexerRowId)))
            .ToListAsync(ct);

        if (matches.Count == 0)
        {
            logger.LogDebug("WantedVideoFulfillmentService (normal): no actionable matches");
            return;
        }

        foreach (var group in matches.GroupBy(m => m.PrdbVideoId))
        {
            var best = PickBest(group, preferred);
            await QueueAsync(client, best.IndexerRow, counters, ct);
        }
    }

    // All-qualities fulfillment: for each video flagged FulfillAllQualities, queue one download
    // per quality (720p, 1080p, 2160p) that does not yet have a download log of any status.
    private async Task RunAllQualitiesAsync(
        DownloadClient client,
        Counters counters,
        CancellationToken ct)
    {
        var videoIds = await db.PrdbWantedVideos
            .Where(w => !w.IsFulfilled && w.FulfillAllQualities)
            .Select(w => w.VideoId)
            .ToListAsync(ct);

        if (videoIds.Count == 0)
        {
            logger.LogDebug("WantedVideoFulfillmentService (all-qualities): no actionable videos");
            return;
        }

        var matches = await db.IndexerRowMatches
            .Include(m => m.IndexerRow)
            .Where(m => videoIds.Contains(m.PrdbVideoId))
            .ToListAsync(ct);

        if (matches.Count == 0) return;

        // Load which indexer rows already have a download log so we can skip them.
        var matchedRowIds = matches.Select(m => m.IndexerRowId).ToHashSet();
        var loggedRowIds = await db.DownloadLogs
            .Where(l => matchedRowIds.Contains(l.IndexerRowId))
            .Select(l => l.IndexerRowId)
            .ToHashSetAsync(ct);

        foreach (var group in matches.GroupBy(m => m.PrdbVideoId))
        {
            foreach (var quality in AllQualities)
            {
                var candidates = group
                    .Where(m => ParseQuality(m.IndexerRow.Title) == quality)
                    .ToList();

                if (candidates.Count == 0) continue;

                // Skip this quality if any row for it already has a download log.
                if (candidates.Any(m => loggedRowIds.Contains(m.IndexerRowId))) continue;

                var best = candidates.First();

                // Track within this run so the same video isn't queued twice if re-encountered.
                loggedRowIds.Add(best.IndexerRowId);

                await QueueAsync(client, best.IndexerRow, counters, ct);
            }
        }
    }

    private async Task QueueAsync(
        DownloadClient client,
        IndexerRow row,
        Counters counters,
        CancellationToken ct)
    {
        var (success, message, clientItemId) = await sender.SendAsync(
            client, row.NzbUrl, row.Title, ct);

        if (!success)
        {
            logger.LogWarning(
                "WantedVideoFulfillmentService: failed to send '{Title}' — {Message}",
                row.Title, message);
            counters.Failed++;
            return;
        }

        var now = DateTime.UtcNow;

        db.DownloadLogs.Add(new DownloadLog
        {
            Id               = Guid.NewGuid(),
            IndexerRowId     = row.Id,
            DownloadClientId = client.Id,
            NzbName          = row.Title,
            NzbUrl           = row.NzbUrl,
            ClientItemId     = clientItemId,
            Status           = DownloadStatus.Queued,
            CreatedAt        = now,
            UpdatedAt        = now,
        });

        logger.LogInformation(
            "WantedVideoFulfillmentService: queued '{Title}' via {Client}",
            row.Title, client.Title);

        counters.Sent++;
    }

    private static IndexerRowMatch PickBest(
        IEnumerable<IndexerRowMatch> matches, VideoQuality preferred)
    {
        var list = matches.ToList();

        // Prefer an exact quality match first
        var exact = list.FirstOrDefault(m => ParseQuality(m.IndexerRow.Title) == preferred);
        if (exact is not null)
            return exact;

        // Otherwise pick the highest quality available
        return list
            .OrderByDescending(m => (int)(ParseQuality(m.IndexerRow.Title) ?? (VideoQuality)(-1)))
            .First();
    }

    /// <summary>Parses a video quality indicator from an indexer row title.</summary>
    internal static VideoQuality? ParseQuality(string title)
    {
        var t = title.ToLowerInvariant();
        if (t.Contains("2160p") || t.Contains("4k") || t.Contains("uhd")) return VideoQuality.P2160;
        if (t.Contains("1080p") || t.Contains("1080i"))                    return VideoQuality.P1080;
        if (t.Contains("720p")  || t.Contains("720i"))                     return VideoQuality.P720;
        return null;
    }
}
