using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.DownloadClients;

public class NzbgetPoller(IHttpClientFactory httpClientFactory, ILogger<NzbgetPoller> logger)
{
    /// <summary>
    /// Polls the NZBGet queue and history for the supplied logs and returns updated snapshots.
    /// Items found in the queue are in progress; items not in the queue are looked up in history.
    /// </summary>
    public async Task<DownloadPollGroupResult> PollAsync(
        DownloadClient client, IEnumerable<DownloadLog> logs, CancellationToken ct)
    {
        var results = new List<DownloadPollResult>();

        var pendingByNzbId = logs
            .Where(l => l.ClientItemId != null)
            .ToDictionary(l => l.ClientItemId!);

        if (pendingByNzbId.Count == 0)
            return new DownloadPollGroupResult(true, results, new HashSet<string>());

        var (queueHits, queueReachable) = await PollQueueAsync(client, pendingByNzbId.Keys, ct);

        if (!queueReachable)
            return DownloadPollGroupResult.ClientUnreachable;

        foreach (var hit in queueHits)
        {
            results.Add(hit);
            pendingByNzbId.Remove(hit.ClientItemId);
        }

        // Items not in queue — check history
        var historyCheckFailed = new HashSet<string>();

        if (pendingByNzbId.Count > 0)
        {
            var (historyResults, historyReachable) =
                await PollHistoryAsync(client, pendingByNzbId.Keys, ct);

            if (historyReachable)
                results.AddRange(historyResults);
            else
                foreach (var id in pendingByNzbId.Keys)
                    historyCheckFailed.Add(id);
        }

        return new DownloadPollGroupResult(true, results, historyCheckFailed);
    }

    private async Task<(List<DownloadPollResult> Results, bool Succeeded)> PollQueueAsync(
        DownloadClient client, IEnumerable<string> nzbIds, CancellationToken ct)
    {
        var results = new List<DownloadPollResult>();
        var wanted = new HashSet<string>(nzbIds);

        try
        {
            var json = await CallAsync(client, "listgroups", new object[] { 0 }, ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("result", out var items)) return (results, true);

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("NZBID", out var idEl)) continue;
                var nzbId = idEl.GetInt64().ToString();
                if (!wanted.Contains(nzbId)) continue;

                var status = MapQueueStatus(
                    item.TryGetProperty("Status", out var stEl) ? stEl.GetString() : null);

                long? total = item.TryGetProperty("FileSizeMb", out var totalEl)
                    ? (long)(totalEl.GetDouble() * 1024 * 1024)
                    : null;

                long? downloaded = null;
                if (total.HasValue && item.TryGetProperty("DownloadedSizeMb", out var dlEl))
                    downloaded = (long)(dlEl.GetDouble() * 1024 * 1024);

                results.Add(new DownloadPollResult
                {
                    ClientItemId    = nzbId,
                    Status          = status,
                    TotalSizeBytes  = total,
                    DownloadedBytes = downloaded,
                });
            }

            return (results, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to poll NZBGet queue for client {ClientId}", client.Id);
            return ([], false);
        }
    }

    private async Task<(List<DownloadPollResult> Results, bool Succeeded)> PollHistoryAsync(
        DownloadClient client, IEnumerable<string> nzbIds, CancellationToken ct)
    {
        var results = new List<DownloadPollResult>();
        var wanted = new HashSet<string>(nzbIds);

        try
        {
            // false = include all history, not just hidden
            var json = await CallAsync(client, "history", new object[] { false }, ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("result", out var items)) return (results, true);

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("NZBID", out var idEl)) continue;
                var nzbId = idEl.GetInt64().ToString();
                if (!wanted.Contains(nzbId)) continue;

                var status = MapHistoryStatus(
                    item.TryGetProperty("Status", out var stEl) ? stEl.GetString() : null);

                long? totalBytes = item.TryGetProperty("FileSizeMb", out var sizeEl)
                    ? (long)(sizeEl.GetDouble() * 1024 * 1024)
                    : null;

                string? destDir = item.TryGetProperty("DestDir", out var destEl)
                    ? destEl.GetString()
                    : null;

                string? errorMessage = null;
                if (status == DownloadStatus.Failed &&
                    item.TryGetProperty("FailMessage", out var failEl))
                {
                    var msg = failEl.GetString();
                    if (!string.IsNullOrWhiteSpace(msg))
                        errorMessage = msg;
                }

                results.Add(new DownloadPollResult
                {
                    ClientItemId    = nzbId,
                    Status          = status,
                    TotalSizeBytes  = totalBytes,
                    DownloadedBytes = status == DownloadStatus.Completed ? totalBytes : null,
                    StoragePath     = destDir,
                    ErrorMessage    = errorMessage,
                });
            }

            return (results, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to poll NZBGet history for client {ClientId}", client.Id);
            return ([], false);
        }
    }

    private async Task<string> CallAsync(
        DownloadClient client, string method, object[] parameters, CancellationToken ct)
    {
        var scheme = client.UseSsl ? "https" : "http";
        var portSegment = client.Port.HasValue ? $":{client.Port.Value}" : string.Empty;
        var url = $"{scheme}://{client.Host}{portSegment}/jsonrpc";

        var body = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters,
            id = 1,
        });

        var http = CreateClient(client);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await http.PostAsync(url, content, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static DownloadStatus MapQueueStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "DOWNLOADING" or "FETCHING" => DownloadStatus.Downloading,
        "PP_QUEUED" or "LOADING_PARS" or "VERIFYING" or "REPAIRING" or
        "VERIFYING_REPAIRED" or "EXECUTING_SCRIPT" or "MOVING" or
        "UNPACKING" or "PP_PAUSE" => DownloadStatus.PostProcessing,
        _ => DownloadStatus.Queued,
    };

    private static DownloadStatus MapHistoryStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "SUCCESS" => DownloadStatus.Completed,
        "FAILURE" or "DELETED" => DownloadStatus.Failed,
        _ => DownloadStatus.PostProcessing,
    };

    private HttpClient CreateClient(DownloadClient client)
    {
        var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        if (!string.IsNullOrEmpty(client.Username))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{client.Username}:{client.Password}"));
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        }

        return http;
    }
}
