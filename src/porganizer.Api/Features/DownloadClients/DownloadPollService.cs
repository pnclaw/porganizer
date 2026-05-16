using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.DownloadLogs;
using porganizer.Api.Features.Library;
using porganizer.Api.Features.WantedFulfillment;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.DownloadClients;

public class DownloadPollService(
    AppDbContext db,
    SabnzbdPoller sabnzbdPoller,
    NzbgetPoller nzbgetPoller,
    DownloadLogFileSyncService downloadLogFileSyncService,
    DownloadFileMoveService downloadFileMoveService,
    LibraryIndexQueueService libraryIndexQueueService,
    IHttpClientFactory httpClientFactory,
    DownloadPollCoordinator pollCoordinator,
    ILogger<DownloadPollService> logger)
{
    public async Task PollAsync(CancellationToken ct)
    {
        using var pollLease = await pollCoordinator.WaitForTurnAsync(ct);

        var pendingLogs = await db.DownloadLogs
            .Include(l => l.DownloadClient)
            .Where(l =>
                l.Status != DownloadStatus.Completed &&
                l.Status != DownloadStatus.Failed &&
                l.ClientItemId != null)
            .ToListAsync(ct);

        if (pendingLogs.Count > 0)
        {
            logger.LogDebug("Polling {Count} pending download(s)", pendingLogs.Count);

            var byClient = pendingLogs.GroupBy(l => l.DownloadClientId);

            foreach (var group in byClient)
            {
                var client = group.First().DownloadClient;

                DownloadPollGroupResult groupResult = client.ClientType switch
                {
                    ClientType.Sabnzbd => await sabnzbdPoller.PollAsync(client, group, ct),
                    ClientType.Nzbget  => await nzbgetPoller.PollAsync(client, group, ct),
                    _                  => new DownloadPollGroupResult(true, [], new HashSet<string>()),
                };

                if (!groupResult.ClientReachable)
                {
                    logger.LogWarning(
                        "DownloadPollService: client {ClientId} unreachable — skipping MissedPollCount for {Count} download(s)",
                        client.Id, group.Count());
                    continue;
                }

                var returnedIds = groupResult.Results.Select(r => r.ClientItemId).ToHashSet();

                foreach (var log in group)
                {
                    if (returnedIds.Contains(log.ClientItemId!))
                    {
                        log.MissedPollCount = 0;
                        ApplyResult(log, groupResult.Results.First(r => r.ClientItemId == log.ClientItemId));
                    }
                    else if (groupResult.HistoryCheckFailedIds.Contains(log.ClientItemId!))
                    {
                        // History lookup threw — status is unknown, not absent; preserve MissedPollCount.
                        logger.LogDebug(
                            "DownloadPollService: history check failed for log {LogId} ('{Name}') — skipping this poll",
                            log.Id, log.NzbName);
                    }
                    else
                    {
                        log.MissedPollCount++;
                        log.UpdatedAt = DateTime.UtcNow;

                        logger.LogDebug(
                            "DownloadPollService: log {LogId} ('{Name}') not found in client — MissedPollCount now {Count} (clientItemId={ClientItemId}, lastStatus={Status})",
                            log.Id, log.NzbName, log.MissedPollCount, log.ClientItemId, log.Status);

                        if (log.MissedPollCount >= 3)
                        {
                            log.Status       = DownloadStatus.Failed;
                            log.ErrorMessage = "Item not found in download client after 3 polls — likely deleted.";
                            log.CompletedAt  = DateTime.UtcNow;
                            logger.LogWarning(
                                "DownloadPollService: marking log {LogId} ('{Name}') as Failed — missing from client after 3 polls (clientItemId={ClientItemId})",
                                log.Id, log.NzbName, log.ClientItemId);
                        }
                    }
                }
            }

            await db.SaveChangesAsync(ct);
        }

        await ProcessCompletedDownloadsAsync(pendingLogs, ct);
    }

    private async Task ProcessCompletedDownloadsAsync(List<DownloadLog> polledLogs, CancellationToken ct)
    {
        var newlyCompleted = polledLogs
            .Where(l => l.Status == DownloadStatus.Completed && l.CompletionPostProcessedAtUtc == null)
            .ToList();

        var newlyCompletedIds = newlyCompleted.Select(l => l.Id).ToHashSet();

        var recoveryCompleted = await db.DownloadLogs
            .Where(l =>
                l.Status == DownloadStatus.Completed &&
                l.CompletionPostProcessedAtUtc == null &&
                !newlyCompletedIds.Contains(l.Id))
            .ToListAsync(ct);

        var completed = newlyCompleted
            .Concat(recoveryCompleted)
            .DistinctBy(l => l.Id)
            .ToList();

        if (completed.Count == 0)
            return;

        logger.LogDebug(
            "Running completion post-processing for {Count} completed download(s)",
            completed.Count);

        var settings = await db.GetSettingsAsync(ct);
        var completedIds = completed.Select(l => l.Id).ToList();
        await downloadLogFileSyncService.SyncAsync(
            completedIds,
            settings.DeleteNonVideoFilesOnCompletion,
            ct);

        var notMovedIds = completed
            .Where(l => l.FilesMovedAtUtc == null)
            .Select(l => l.Id)
            .ToList();

        await downloadFileMoveService.MoveAsync(notMovedIds, settings, ct);
        await FulfillWantedVideosAsync(completed, settings, ct);

        var folderMappings = await db.FolderMappings.ToListAsync(ct);

        var storagePaths = completed
            .Select(l => l.StoragePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => ApplyFolderMapping(p!, folderMappings))
            .Distinct()
            .ToList();

        if (storagePaths.Count > 0)
            await libraryIndexQueueService.EnqueueForPathsAsync(storagePaths, ct);

        var now = DateTime.UtcNow;
        foreach (var log in completed)
        {
            log.CompletionPostProcessedAtUtc = now;
            log.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    private static string ApplyFolderMapping(string path, List<FolderMapping> mappings)
    {
        foreach (var mapping in mappings)
        {
            if (path.StartsWith(mapping.OriginalFolder, StringComparison.OrdinalIgnoreCase))
                return mapping.MappedToFolder + path[mapping.OriginalFolder.Length..];
        }
        return path;
    }

    private static void ApplyResult(DownloadLog log, DownloadPollResult result)
    {
        log.Status         = result.Status;
        log.TotalSizeBytes = result.TotalSizeBytes ?? log.TotalSizeBytes;
        log.LastPolledAt   = DateTime.UtcNow;
        log.UpdatedAt      = DateTime.UtcNow;

        if (result.DownloadedBytes.HasValue)
            log.DownloadedBytes = result.DownloadedBytes;

        if (result.StoragePath != null)
            log.StoragePath = result.StoragePath;

        if (result.ErrorMessage != null)
            log.ErrorMessage = result.ErrorMessage;

        if (result.Status is DownloadStatus.Completed or DownloadStatus.Failed)
            log.CompletedAt = DateTime.UtcNow;
    }

    private async Task FulfillWantedVideosAsync(List<DownloadLog> completedLogs, AppSettings settings, CancellationToken ct)
    {
        var rowIds = completedLogs.Select(l => l.IndexerRowId).ToList();

        var matches = await db.Set<IndexerRowMatch>()
            .Include(m => m.IndexerRow)
            .Where(m => rowIds.Contains(m.IndexerRowId))
            .ToListAsync(ct);

        if (matches.Count == 0) return;

        var videoIds = matches.Select(m => m.PrdbVideoId).ToList();

        var wanted = await db.PrdbWantedVideos
            .Where(w => videoIds.Contains(w.VideoId) && !w.IsFulfilled)
            .ToListAsync(ct);

        if (wanted.Count == 0) return;

        var now = DateTime.UtcNow;

        foreach (var w in wanted)
        {
            var match = matches.First(m => m.PrdbVideoId == w.VideoId);
            var log   = completedLogs.First(l => l.IndexerRowId == match.IndexerRowId);

            w.IsFulfilled           = true;
            w.FulfilledAtUtc        = now;
            w.FulfillmentExternalId = log.Id.ToString();
            w.FulfilledInQuality    = (int?)WantedVideoFulfillmentService.ParseQuality(match.IndexerRow.Title);
        }

        await db.SaveChangesAsync(ct);
        await NotifyPrdbFulfillmentAsync(wanted, settings, ct);
    }

    private async Task NotifyPrdbFulfillmentAsync(List<PrdbWantedVideo> fulfilled, AppSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("DownloadPollService: PrdbApiKey not configured — skipping fulfilment notification");
            return;
        }

        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);

        foreach (var w in fulfilled)
        {
            try
            {
                var response = await http.PutAsJsonAsync(
                    $"wanted-videos/{w.VideoId}",
                    new
                    {
                        isFulfilled           = true,
                        fulfilledAtUtc        = w.FulfilledAtUtc,
                        fulfilledInQuality    = w.FulfilledInQuality,
                        fulfillmentExternalId = w.FulfillmentExternalId,
                    },
                    ct);

                if (!response.IsSuccessStatusCode)
                    logger.LogWarning(
                        "DownloadPollService: failed to notify prdb.net of fulfilment for video {VideoId} — {StatusCode}",
                        w.VideoId, response.StatusCode);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "DownloadPollService: error notifying prdb.net of fulfilment for video {VideoId}",
                    w.VideoId);
            }
        }
    }
}
