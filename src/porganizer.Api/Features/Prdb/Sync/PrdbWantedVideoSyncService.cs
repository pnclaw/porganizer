using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb.Sync;

public class PrdbWantedVideoSyncService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<PrdbWantedVideoSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly DateTime InitialSinceUtc = DateTime.UnixEpoch;
    private const int ChangesPageSize = 1000;

    public async Task RunAsync(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("PrdbWantedVideoSyncService: PrdbApiKey not configured — skipping");
            return;
        }

        var http = CreateClient(settings);
        await RunChangeFeedAsync(http, settings, ct);
    }

    private async Task RunChangeFeedAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        logger.LogInformation(
            "PrdbWantedVideoSyncService: syncing change feed from {Since} / {SinceId}",
            settings.PrdbWantedVideoSyncCursorUtc?.ToString("O") ?? "<start>",
            settings.PrdbWantedVideoSyncCursorId?.ToString() ?? "<none>");

        while (true)
        {
            var url = BuildChangesUrl(
                settings.PrdbWantedVideoSyncCursorUtc,
                settings.PrdbWantedVideoSyncCursorId,
                ChangesPageSize);
            logger.LogInformation(
                "PrdbWantedVideoSyncService: requesting {Url}",
                BuildAbsoluteUrl(http, url));

            using var httpResponse = await http.GetAsync(url, ct);
            httpResponse.EnsureSuccessStatusCode();

            var responseDateUtc = httpResponse.Headers.Date?.UtcDateTime;
            var response = await httpResponse.Content.ReadFromJsonAsync<PrdbApiWantedVideoChangesResponse>(
                JsonOptions,
                ct);
            logger.LogInformation(
                "PrdbWantedVideoSyncService: received {Count} wanted video change(s), hasMore={HasMore}, nextCursor={NextCursorUpdatedAt} / {NextCursorId}",
                response?.Items.Count ?? 0,
                response?.HasMore ?? false,
                response?.NextCursor?.UpdatedAtUtc.ToString("O") ?? "<none>",
                response?.NextCursor?.Id.ToString() ?? "<none>");

            if (response is null || response.Items.Count == 0)
            {
                var now = responseDateUtc ?? DateTime.UtcNow;

                if (settings.PrdbWantedVideoSyncCursorUtc is null)
                {
                    settings.PrdbWantedVideoSyncCursorUtc = now;
                    settings.PrdbWantedVideoSyncCursorId = null;
                }

                settings.PrdbWantedVideoLastSyncedAt = now;
                await db.SaveChangesAsync(ct);
                break;
            }

            await ApplyChangesAsync(http, response.Items, ct);

            var cursor = response.NextCursor ?? GetCursorFromLastItem(response.Items);
            settings.PrdbWantedVideoSyncCursorUtc = cursor.UpdatedAtUtc;
            settings.PrdbWantedVideoSyncCursorId = cursor.Id;
            settings.PrdbWantedVideoLastSyncedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            if (!response.HasMore)
                break;
        }

        logger.LogInformation(
            "PrdbWantedVideoSyncService: change sync complete at {Since:O} / {SinceId}",
            settings.PrdbWantedVideoSyncCursorUtc,
            settings.PrdbWantedVideoSyncCursorId);
    }

    private async Task ApplyChangesAsync(HttpClient http, List<PrdbApiWantedVideoChangeDto> items, CancellationToken ct)
    {
        var wantedDtos = items.Select(i => i.WantedVideo).ToList();
        var liveVideoIds = wantedDtos
            .Where(i => !i.IsDeleted)
            .Select(i => i.VideoId)
            .Distinct()
            .ToHashSet();

        await EnsureVideoStubsAsync(http, liveVideoIds, ct);

        var knownVideoIds = await db.PrdbVideos
            .Where(v => liveVideoIds.Contains(v.Id))
            .Select(v => v.Id)
            .ToHashSetAsync(ct);

        var ids = wantedDtos.Select(i => i.VideoId).Distinct().ToList();
        var existing = await db.PrdbWantedVideos
            .Where(w => ids.Contains(w.VideoId))
            .ToDictionaryAsync(w => w.VideoId, ct);

        var now = DateTime.UtcNow;

        foreach (var item in wantedDtos)
        {
            if (item.IsDeleted)
            {
                if (existing.TryGetValue(item.VideoId, out var toDelete))
                {
                    db.PrdbWantedVideos.Remove(toDelete);
                    existing.Remove(item.VideoId);
                }

                continue;
            }

            if (!knownVideoIds.Contains(item.VideoId))
            {
                throw new InvalidOperationException(
                    $"Wanted-video change for {item.VideoId} could not be applied because the video stub is still missing.");
            }

            if (existing.TryGetValue(item.VideoId, out var entity))
            {
                // Local fulfillment, set from the download pipeline, takes precedence over remote reset events.
                if (!entity.IsFulfilled)
                {
                    entity.IsFulfilled = item.IsFulfilled;
                    entity.FulfilledAtUtc = item.FulfilledAtUtc;
                    entity.FulfilledInQuality = item.FulfilledInQuality;
                    entity.FulfillmentExternalId = item.FulfillmentExternalId;
                    entity.FulfillmentByApp = item.FulfillmentByApp;
                }

                entity.PrdbUpdatedAtUtc = item.UpdatedAtUtc;
                entity.SyncedAtUtc = now;
                continue;
            }

            entity = new PrdbWantedVideo
            {
                VideoId = item.VideoId,
                IsFulfilled = item.IsFulfilled,
                FulfilledAtUtc = item.FulfilledAtUtc,
                FulfilledInQuality = item.FulfilledInQuality,
                FulfillmentExternalId = item.FulfillmentExternalId,
                FulfillmentByApp = item.FulfillmentByApp,
                PrdbCreatedAtUtc = item.CreatedAtUtc,
                PrdbUpdatedAtUtc = item.UpdatedAtUtc,
                SyncedAtUtc = now,
            };

            db.PrdbWantedVideos.Add(entity);
            existing[item.VideoId] = entity;
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    private async Task EnsureVideoStubsAsync(HttpClient http, HashSet<Guid> videoIds, CancellationToken ct)
    {
        if (videoIds.Count == 0)
            return;

        var existingVideoIds = await db.PrdbVideos
            .Where(v => videoIds.Contains(v.Id))
            .Select(v => v.Id)
            .ToHashSetAsync(ct);

        var missingIds = videoIds.Except(existingVideoIds).ToList();
        if (missingIds.Count == 0)
            return;

        logger.LogInformation(
            "PrdbWantedVideoSyncService: fetching details for {Count} wanted video(s) missing locally",
            missingIds.Count);

        var existingSiteIds = await db.PrdbSites
            .Select(s => s.Id)
            .ToHashSetAsync(ct);

        var now = DateTime.UtcNow;

        foreach (var videoId in missingIds)
        {
            ct.ThrowIfCancellationRequested();

            var detail = await http.GetFromJsonAsync<PrdbApiVideoDetail>(
                $"videos/{videoId}",
                JsonOptions,
                ct);

            if (detail is null)
                throw new InvalidOperationException($"No detail returned for wanted video {videoId}.");

            if (!existingSiteIds.Contains(detail.Site.Id))
            {
                db.PrdbSites.Add(new PrdbSite
                {
                    Id = detail.Site.Id,
                    Title = detail.Site.Title,
                    Url = detail.Site.Url,
                    SyncedAtUtc = now,
                });
                existingSiteIds.Add(detail.Site.Id);
            }

            db.PrdbVideos.Add(new PrdbVideo
            {
                Id = detail.Id,
                Title = detail.Title,
                ReleaseDate = detail.ReleaseDate,
                SiteId = detail.Site.Id,
                PrdbCreatedAtUtc = detail.CreatedAtUtc,
                PrdbUpdatedAtUtc = detail.UpdatedAtUtc,
                SyncedAtUtc = now,
            });
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }


    private static PrdbApiWantedVideoChangesCursorDto GetCursorFromLastItem(List<PrdbApiWantedVideoChangeDto> items)
    {
        var last = items[^1].WantedVideo;
        return new PrdbApiWantedVideoChangesCursorDto(last.UpdatedAtUtc, last.VideoId);
    }

    private static string BuildChangesUrl(DateTime? since, Guid? sinceId, int pageSize)
    {
        var effectiveSince = NormalizeUtc(since ?? InitialSinceUtc);
        var url = $"wanted-videos/changes?PageSize={pageSize}&Since={Uri.EscapeDataString(effectiveSince.ToString("O"))}";

        if (sinceId.HasValue)
            url += $"&SinceId={sinceId.Value}";

        return url;
    }

    private static Uri BuildAbsoluteUrl(HttpClient http, string relativeUrl)
        => new(http.BaseAddress!, relativeUrl);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private HttpClient CreateClient(AppSettings settings)
    {
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);
        return http;
    }
}
