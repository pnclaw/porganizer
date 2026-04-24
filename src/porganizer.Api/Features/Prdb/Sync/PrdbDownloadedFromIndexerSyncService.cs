using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using porganizer.Api.Common;
using porganizer.Api.Features.DownloadClients;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.Prdb.Sync;

public class PrdbDownloadedFromIndexerSyncService(
    AppDbContext db,
    DownloadLogFileSyncService downloadLogFileSyncService,
    IHttpClientFactory httpClientFactory,
    ILogger<PrdbDownloadedFromIndexerSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("PrdbDownloadedFromIndexerSyncService: PrdbApiKey not configured — skipping");
            return;
        }

        var completedLogIds = await db.DownloadLogs
            .Where(l => l.Status == DownloadStatus.Completed)
            .Select(l => l.Id)
            .ToListAsync(ct);

        if (completedLogIds.Count == 0)
            return;

        var fileSyncResults = await downloadLogFileSyncService.SyncAsync(completedLogIds, false, ct);
        db.ChangeTracker.Clear();

        var logs = await db.DownloadLogs
            .Include(l => l.IndexerRow)
                .ThenInclude(r => r.Indexer)
            .Include(l => l.Files)
            .Where(l => completedLogIds.Contains(l.Id))
            .Where(l => l.PrdbDownloadedFromIndexerId == null
                || l.PrdbDownloadedFromIndexerSyncError != null
                || l.PrdbDownloadedFromIndexerSyncedAtUtc == null)
            .ToListAsync(ct);

        var rowIds = logs.Select(l => l.IndexerRowId).Distinct().ToList();
        var matchByRowId = await db.IndexerRowMatches
            .Where(m => rowIds.Contains(m.IndexerRowId))
            .ToDictionaryAsync(m => m.IndexerRowId, ct);

        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);

        foreach (var log in logs)
        {
            matchByRowId.TryGetValue(log.IndexerRowId, out var match);
            var videoId = match?.PrdbVideoId;

            if (!TryMapIndexerSource(log.IndexerRow.Indexer.Url, out var indexerSource))
            {
                log.PrdbDownloadedFromIndexerSyncAttemptedAtUtc = DateTime.UtcNow;
                log.PrdbDownloadedFromIndexerSyncError = $"Unsupported indexer URL '{log.IndexerRow.Indexer.Url}'";
                await db.SaveChangesAsync(ct);
                logger.LogWarning(
                    "PrdbDownloadedFromIndexerSyncService: unsupported indexer URL '{Url}' for log {LogId}",
                    log.IndexerRow.Indexer.Url, log.Id);
                continue;
            }

            if (string.IsNullOrWhiteSpace(log.ClientItemId))
            {
                log.PrdbDownloadedFromIndexerSyncAttemptedAtUtc = DateTime.UtcNow;
                log.PrdbDownloadedFromIndexerSyncError = "Download log has no client item id";
                await db.SaveChangesAsync(ct);
                logger.LogWarning("PrdbDownloadedFromIndexerSyncService: missing ClientItemId for log {LogId}", log.Id);
                continue;
            }

            var fileSyncResult = fileSyncResults.GetValueOrDefault(log.Id);
            if (fileSyncResult is null || !fileSyncResult.DirectoryExists || !log.Files.Any())
            {
                logger.LogDebug(
                    "PrdbDownloadedFromIndexerSyncService: log {LogId} not ready for sync yet — files not available",
                    log.Id);
                continue;
            }

            var fingerprint = ComputeFingerprint(log, videoId, indexerSource);
            if (log.PrdbDownloadedFromIndexerId != null &&
                log.PrdbDownloadedFromIndexerSyncError == null &&
                string.Equals(log.PrdbDownloadedFromIndexerSyncFingerprint, fingerprint, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                log.PrdbDownloadedFromIndexerSyncAttemptedAtUtc = DateTime.UtcNow;

                if (log.PrdbDownloadedFromIndexerId is null)
                {
                    var created = await CreateParentAsync(http, log, videoId, indexerSource, ct);
                    ApplyParentResponse(log, created, fingerprint);
                }
                else
                {
                    var existingRemoteFileIds = log.Files
                        .Where(f => f.PrdbDownloadedFromIndexerFilenameId != null)
                        .Select(f => f.Id)
                        .ToHashSet();

                    foreach (var removed in fileSyncResult.RemovedFiles.Where(r => r.PrdbDownloadedFromIndexerFilenameId != null))
                        await DeleteFilenameAsync(http, log.PrdbDownloadedFromIndexerId.Value, removed.PrdbDownloadedFromIndexerFilenameId!.Value, ct);

                    var updated = await UpdateParentAsync(http, log, indexerSource, ct);
                    ApplyParentResponse(log, updated, null);

                    foreach (var file in log.Files
                                 .Where(f => f.PrdbDownloadedFromIndexerFilenameId == null)
                                 .OrderBy(f => f.FileName))
                    {
                        var response = await AddFilenameAsync(http, log.PrdbDownloadedFromIndexerId.Value, file, ct);
                        ApplyParentResponse(log, response, null);
                    }

                    foreach (var file in log.Files
                                 .Where(f => f.PrdbDownloadedFromIndexerFilenameId != null && existingRemoteFileIds.Contains(f.Id))
                                 .OrderBy(f => f.FileName))
                    {
                        var response = await UpdateFilenameAsync(
                            http,
                            log.PrdbDownloadedFromIndexerId.Value,
                            file.PrdbDownloadedFromIndexerFilenameId!.Value,
                            file,
                            ct);
                        ApplyParentResponse(log, response, null);
                    }

                    log.PrdbDownloadedFromIndexerSyncFingerprint = fingerprint;
                    log.PrdbDownloadedFromIndexerSyncedAtUtc = DateTime.UtcNow;
                    log.PrdbDownloadedFromIndexerSyncError = null;
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                log.PrdbDownloadedFromIndexerSyncError = ex.Message;
                await db.SaveChangesAsync(ct);
                logger.LogWarning(ex,
                    "PrdbDownloadedFromIndexerSyncService: failed syncing log {LogId}",
                    log.Id);
            }
        }

    }

    private void ApplyParentResponse(DownloadLog log, DownloadedFromIndexerResponse response, string? fingerprint)
    {
        log.PrdbDownloadedFromIndexerId = response.Id;
        log.PrdbDownloadedFromIndexerSyncError = null;
        log.PrdbDownloadedFromIndexerSyncedAtUtc = DateTime.UtcNow;

        if (fingerprint is not null)
            log.PrdbDownloadedFromIndexerSyncFingerprint = fingerprint;

        var remoteByFilename = response.Filenames.ToDictionary(f => f.Filename, StringComparer.OrdinalIgnoreCase);
        foreach (var file in log.Files)
        {
            if (remoteByFilename.TryGetValue(file.OriginalFileName ?? file.FileName, out var remote))
                file.PrdbDownloadedFromIndexerFilenameId = remote.Id;
        }
    }

    private async Task<DownloadedFromIndexerResponse> CreateParentAsync(
        HttpClient http,
        DownloadLog log,
        Guid? videoId,
        int indexerSource,
        CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            "downloaded-from-indexers",
            new AddDownloadedFromIndexerRequest(
                videoId,
                indexerSource,
                log.IndexerRow.NzbId,
                log.ClientItemId!,
                log.NzbName,
                SanitizeNzbUrl(log.NzbUrl, log.IndexerRow.Indexer.ApiKey),
                log.Files
                    .OrderBy(f => f.FileName)
                    .Select(ToRequest)
                    .ToList()),
            JsonOptions,
            ct);

        return await ReadResponseAsync(response, ct);
    }

    private async Task<DownloadedFromIndexerResponse> UpdateParentAsync(
        HttpClient http,
        DownloadLog log,
        int indexerSource,
        CancellationToken ct)
    {
        using var response = await http.PutAsJsonAsync(
            $"downloaded-from-indexers/{log.PrdbDownloadedFromIndexerId}",
            new UpdateDownloadedFromIndexerRequest(
                indexerSource,
                log.IndexerRow.NzbId,
                log.ClientItemId!,
                log.NzbName,
                SanitizeNzbUrl(log.NzbUrl, log.IndexerRow.Indexer.ApiKey)),
            JsonOptions,
            ct);

        return await ReadResponseAsync(response, ct);
    }

    private async Task<DownloadedFromIndexerResponse> AddFilenameAsync(
        HttpClient http,
        Guid downloadedFromIndexerId,
        DownloadLogFile file,
        CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            $"downloaded-from-indexers/{downloadedFromIndexerId}/filenames",
            ToRequest(file),
            JsonOptions,
            ct);

        return await ReadResponseAsync(response, ct);
    }

    private async Task<DownloadedFromIndexerResponse> UpdateFilenameAsync(
        HttpClient http,
        Guid downloadedFromIndexerId,
        Guid filenameId,
        DownloadLogFile file,
        CancellationToken ct)
    {
        using var response = await http.PutAsJsonAsync(
            $"downloaded-from-indexers/{downloadedFromIndexerId}/filenames/{filenameId}",
            ToRequest(file),
            JsonOptions,
            ct);

        return await ReadResponseAsync(response, ct);
    }

    private async Task DeleteFilenameAsync(HttpClient http, Guid downloadedFromIndexerId, Guid filenameId, CancellationToken ct)
    {
        using var response = await http.DeleteAsync(
            $"downloaded-from-indexers/{downloadedFromIndexerId}/filenames/{filenameId}",
            ct);

        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"prdb.net returned {(int)response.StatusCode} deleting filename {filenameId}: {body}",
            null,
            response.StatusCode);
    }

    private static AddDownloadedFromIndexerFilenameRequest ToRequest(DownloadLogFile file) =>
        new(file.OriginalFileName ?? file.FileName, file.FileSize, file.OsHash, file.PHash);

    private static async Task<DownloadedFromIndexerResponse> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            var parsed = await response.Content.ReadFromJsonAsync<DownloadedFromIndexerResponse>(JsonOptions, ct);
            if (parsed is null)
                throw new InvalidOperationException("prdb.net returned an empty response body");

            return parsed;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"prdb.net returned {(int)response.StatusCode}: {body}",
            null,
            response.StatusCode);
    }

    internal static bool TryMapIndexerSource(string url, out int indexerSource) =>
        IndexerSourceMapper.TryMap(url, out indexerSource);

    private static string ComputeFingerprint(DownloadLog log, Guid? videoId, int indexerSource)
    {
        var builder = new StringBuilder()
            .Append(videoId).Append('|')
            .Append(indexerSource).Append('|')
            .Append(log.IndexerRow.NzbId).Append('|')
            .Append(log.ClientItemId).Append('|')
            .Append(log.NzbName).Append('|')
            .Append(log.NzbUrl);

        foreach (var file in log.Files.OrderBy(f => f.OriginalFileName ?? f.FileName, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('|')
                .Append(file.OriginalFileName ?? file.FileName)
                .Append(':')
                .Append(file.FileSize)
                .Append(':')
                .Append(file.OsHash)
                .Append(':')
                .Append(file.PHash);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }

    internal static string SanitizeNzbUrl(string url, string indexerApiKey)
    {
        if (string.IsNullOrEmpty(indexerApiKey))
            return url;

        return url.Replace(indexerApiKey, "[apikey]", StringComparison.Ordinal);
    }

    private sealed record AddDownloadedFromIndexerRequest(
        Guid? VideoId,
        int IndexerSource,
        string IndexerId,
        string DownloadIdentifier,
        string NzbName,
        string NzbUrl,
        List<AddDownloadedFromIndexerFilenameRequest> Filenames);

    private sealed record UpdateDownloadedFromIndexerRequest(
        int IndexerSource,
        string IndexerId,
        string DownloadIdentifier,
        string NzbName,
        string NzbUrl);

    private sealed record AddDownloadedFromIndexerFilenameRequest(
        string Filename,
        long Filesize,
        string? OsHash,
        string? PHash);

    private sealed class DownloadedFromIndexerResponse
    {
        public Guid Id { get; set; }
        public List<DownloadedFromIndexerFilenameDto> Filenames { get; set; } = [];
    }

    private sealed class DownloadedFromIndexerFilenameDto
    {
        public Guid Id { get; set; }
        public string Filename { get; set; } = string.Empty;
    }

}
