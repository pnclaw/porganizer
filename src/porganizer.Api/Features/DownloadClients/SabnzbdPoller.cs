using System.Text.Json;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.DownloadClients;

public class SabnzbdPoller(IHttpClientFactory httpClientFactory, ILogger<SabnzbdPoller> logger)
{
    /// <summary>
    /// Polls the SABnzbd queue and history for the supplied logs and returns updated snapshots.
    /// Items found in the queue are in progress; items not in the queue are looked up in history.
    /// </summary>
    public async Task<DownloadPollGroupResult> PollAsync(
        DownloadClient client, IEnumerable<DownloadLog> logs, CancellationToken ct)
    {
        var results = new List<DownloadPollResult>();

        var pendingById = logs
            .Where(l => l.ClientItemId != null)
            .ToDictionary(l => l.ClientItemId!);

        if (pendingById.Count == 0)
            return new DownloadPollGroupResult(true, results, new HashSet<string>());

        var (queueHits, queueReachable) = await PollQueueAsync(client, pendingById.Keys, ct);

        if (!queueReachable)
            return DownloadPollGroupResult.ClientUnreachable;

        foreach (var hit in queueHits)
        {
            results.Add(hit);
            pendingById.Remove(hit.ClientItemId);
        }

        // Items not in queue — check history
        var historyCheckFailed = new HashSet<string>();

        foreach (var (nzoId, _) in pendingById)
        {
            var (historyResult, historyReachable) = await PollHistoryAsync(client, nzoId, ct);

            if (historyResult != null)
                results.Add(historyResult);
            else if (!historyReachable)
                historyCheckFailed.Add(nzoId);
            // historyReachable && historyResult == null → item genuinely absent from history
        }

        return new DownloadPollGroupResult(true, results, historyCheckFailed);
    }

    private async Task<(List<DownloadPollResult> Results, bool Succeeded)> PollQueueAsync(
        DownloadClient client, IEnumerable<string> nzoIds, CancellationToken ct)
    {
        var results = new List<DownloadPollResult>();
        var scheme = client.UseSsl ? "https" : "http";
        var portSegment = client.Port.HasValue ? $":{client.Port.Value}" : string.Empty;
        var url = $"{scheme}://{client.Host}{portSegment}/api?mode=queue&output=json&apikey={client.ApiKey}";

        try
        {
            var http = CreateClient();
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("queue", out var queue)) return (results, true);
            if (!queue.TryGetProperty("slots", out var slots)) return (results, true);

            var wanted = new HashSet<string>(nzoIds);

            foreach (var slot in slots.EnumerateArray())
            {
                var nzoId = slot.TryGetProperty("nzo_id", out var nzoEl) ? nzoEl.GetString() : null;
                if (nzoId == null || !wanted.Contains(nzoId)) continue;

                var status = MapQueueStatus(
                    slot.TryGetProperty("status", out var stEl) ? stEl.GetString() : null);

                long? total = null;
                long? downloaded = null;
                if (slot.TryGetProperty("mb", out var mbEl) &&
                    double.TryParse(mbEl.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var totalMb))
                {
                    total = (long)(totalMb * 1024 * 1024);
                }
                if (total.HasValue &&
                    slot.TryGetProperty("mbleft", out var mbLeftEl) &&
                    double.TryParse(mbLeftEl.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var leftMb))
                {
                    downloaded = total.Value - (long)(leftMb * 1024 * 1024);
                }

                results.Add(new DownloadPollResult
                {
                    ClientItemId    = nzoId,
                    Status          = status,
                    TotalSizeBytes  = total,
                    DownloadedBytes = downloaded,
                });
            }

            logger.LogDebug(
                "SabnzbdPoller: queue poll for client {ClientId} matched {Found}/{Wanted} wanted item(s)",
                client.Id, results.Count, wanted.Count);

            return (results, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to poll SABnzbd queue for client {ClientId}", client.Id);
            return ([], false);
        }
    }

    /// <returns>
    /// (result, succeeded): result is non-null when the item was found; succeeded is false
    /// when the HTTP request itself failed, meaning the item's status is unknown (not absent).
    /// </returns>
    private async Task<(DownloadPollResult? Result, bool Succeeded)> PollHistoryAsync(
        DownloadClient client, string nzoId, CancellationToken ct)
    {
        var scheme = client.UseSsl ? "https" : "http";
        var portSegment = client.Port.HasValue ? $":{client.Port.Value}" : string.Empty;
        var url = $"{scheme}://{client.Host}{portSegment}/api" +
                  $"?mode=history&output=json&apikey={client.ApiKey}&nzo_id={nzoId}";

        try
        {
            var http = CreateClient();
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("history", out var history)) return (null, true);
            if (!history.TryGetProperty("slots", out var slots)) return (null, true);

            foreach (var slot in slots.EnumerateArray())
            {
                var id = slot.TryGetProperty("nzo_id", out var idEl) ? idEl.GetString() : null;
                if (id != nzoId) continue;

                var status = MapHistoryStatus(
                    slot.TryGetProperty("status", out var stEl) ? stEl.GetString() : null);

                long? totalBytes = slot.TryGetProperty("bytes", out var bytesEl)
                    ? bytesEl.GetInt64()
                    : null;

                string? storagePath = slot.TryGetProperty("storage", out var storEl)
                    ? storEl.GetString()
                    : null;

                string? errorMessage = null;
                if (status == DownloadStatus.Failed &&
                    slot.TryGetProperty("fail_message", out var failEl))
                {
                    var msg = failEl.GetString();
                    if (!string.IsNullOrWhiteSpace(msg))
                        errorMessage = msg;
                }

                return (new DownloadPollResult
                {
                    ClientItemId    = nzoId,
                    Status          = status,
                    TotalSizeBytes  = totalBytes,
                    DownloadedBytes = status == DownloadStatus.Completed ? totalBytes : null,
                    StoragePath     = storagePath,
                    ErrorMessage    = errorMessage,
                }, true);
            }

            // Request succeeded but item was not present in history.
            // Log the IDs that were returned so we can diagnose nzo_id mismatches.
            var returned = slots.EnumerateArray()
                .Select(s => s.TryGetProperty("nzo_id", out var el) ? el.GetString() : null)
                .Where(id => id != null)
                .ToList();

            if (returned.Count > 0)
            {
                logger.LogDebug(
                    "SabnzbdPoller: history lookup for nzo_id {NzoId} returned {Count} slot(s) but none matched. Returned ids: [{ReturnedIds}]",
                    nzoId, returned.Count, string.Join(", ", returned));
            }
            else
            {
                logger.LogDebug(
                    "SabnzbdPoller: history lookup for nzo_id {NzoId} returned an empty slots array",
                    nzoId);
            }

            return (null, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to poll SABnzbd history for nzo_id {NzoId}", nzoId);
            return (null, false);
        }
    }

    private static DownloadStatus MapQueueStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "DOWNLOADING" or "FETCHING" or "GRABBING" => DownloadStatus.Downloading,
        "VERIFYING" or "REPAIRING" or "EXTRACTING" or "MOVING" or "RUNNING" => DownloadStatus.PostProcessing,
        _ => DownloadStatus.Queued,
    };

    private static DownloadStatus MapHistoryStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "COMPLETED" => DownloadStatus.Completed,
        "FAILED"    => DownloadStatus.Failed,
        _           => DownloadStatus.PostProcessing,
    };

    private HttpClient CreateClient()
    {
        var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        return http;
    }
}
