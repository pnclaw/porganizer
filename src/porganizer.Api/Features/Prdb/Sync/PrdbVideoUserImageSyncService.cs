using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb.Sync;

public class PrdbVideoUserImageSyncService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<PrdbVideoUserImageSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly DateTime InitialSinceUtc = DateTime.UnixEpoch;
    private const int ChangesPageSize = 1000;

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("PrdbVideoUserImageSyncService: PrdbApiKey not configured — skipping");
            return 0;
        }

        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);

        return await RunIncrementalAsync(http, settings, ct);
    }

    private async Task<int> RunIncrementalAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        logger.LogInformation(
            "PrdbVideoUserImageSyncService: incremental sync from {Since:O} / {SinceId}",
            settings.PrdbVideoUserImageSyncCursorUtc?.ToString("O") ?? "<start>",
            settings.PrdbVideoUserImageSyncCursorId?.ToString() ?? "<none>");

        var totalApplied = 0;

        while (true)
        {
            var url = BuildChangesUrl(
                settings.PrdbVideoUserImageSyncCursorUtc,
                settings.PrdbVideoUserImageSyncCursorId,
                ChangesPageSize);

            using var httpResponse = await http.GetAsync(url, ct);
            httpResponse.EnsureSuccessStatusCode();

            var responseDateUtc = httpResponse.Headers.Date?.UtcDateTime;
            var response = await httpResponse.Content.ReadFromJsonAsync<PrdbApiVideoUserImageChangesResponse>(
                JsonOptions, ct);

            if (response is null || response.Items.Count == 0)
            {
                if (settings.PrdbVideoUserImageSyncCursorUtc is null)
                {
                    settings.PrdbVideoUserImageSyncCursorUtc = responseDateUtc ?? DateTime.UtcNow;
                    settings.PrdbVideoUserImageSyncCursorId = null;
                    await db.SaveChangesAsync(ct);
                }

                break;
            }

            var applied = await ApplyChangesAsync(response.Items, ct);
            totalApplied += applied;

            var cursor = response.NextCursor ?? GetCursorFromLastItem(response.Items);
            settings.PrdbVideoUserImageSyncCursorUtc = cursor.UpdatedAtUtc;
            settings.PrdbVideoUserImageSyncCursorId = cursor.Id;
            await db.SaveChangesAsync(ct);

            if (!response.HasMore)
                break;
        }

        logger.LogInformation(
            "PrdbVideoUserImageSyncService: sync complete — {Count} change(s) applied, cursor at {Since:O} / {SinceId}",
            totalApplied,
            settings.PrdbVideoUserImageSyncCursorUtc,
            settings.PrdbVideoUserImageSyncCursorId);

        return totalApplied;
    }

    private async Task<int> ApplyChangesAsync(List<PrdbApiVideoUserImageChangeDto> items, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids = items.Select(i => i.VideoUserImage.Id).Distinct().ToList();

        var existing = await db.PrdbVideoUserImages
            .Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        // Only upsert images for videos we know about — avoids orphaned rows for unrelated videos.
        // VideoId is null for images uploaded against unmatched files; skip those entirely.
        var videoIds = items
            .Where(i => !i.VideoUserImage.IsDeleted && i.VideoUserImage.VideoId.HasValue)
            .Select(i => i.VideoUserImage.VideoId!.Value)
            .Distinct()
            .ToList();

        var knownVideoIds = await db.PrdbVideos
            .Where(v => videoIds.Contains(v.Id))
            .Select(v => v.Id)
            .ToHashSetAsync(ct);

        var applied = 0;

        foreach (var item in items)
        {
            var dto = item.VideoUserImage;

            if (dto.IsDeleted)
            {
                if (existing.TryGetValue(dto.Id, out var toDelete))
                {
                    db.PrdbVideoUserImages.Remove(toDelete);
                    existing.Remove(dto.Id);
                    applied++;
                }

                continue;
            }

            if (dto.VideoId is null || !knownVideoIds.Contains(dto.VideoId.Value))
                continue;

            if (existing.TryGetValue(dto.Id, out var entity))
            {
                Map(entity, dto, now);
            }
            else
            {
                entity = new PrdbVideoUserImage();
                Map(entity, dto, now);
                db.PrdbVideoUserImages.Add(entity);
                existing[dto.Id] = entity;
            }

            applied++;
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);

        return applied;
    }

    private static void Map(PrdbVideoUserImage entity, PrdbApiVideoUserImageDto dto, DateTime now)
    {
        entity.Id = dto.Id;
        entity.VideoId = dto.VideoId!.Value;
        entity.Url = dto.Url;
        entity.PreviewImageType = dto.PreviewImageType;
        entity.DisplayOrder = dto.DisplayOrder;
        entity.ModerationVisibility = dto.ModerationVisibility;
        entity.SpriteTileCount = dto.SpriteTileCount;
        entity.SpriteTileWidth = dto.SpriteTileWidth;
        entity.SpriteTileHeight = dto.SpriteTileHeight;
        entity.SpriteColumns = dto.SpriteColumns;
        entity.SpriteRows = dto.SpriteRows;
        entity.PrdbUpdatedAtUtc = dto.UpdatedAtUtc;
        entity.SyncedAtUtc = now;
    }

    private static PrdbApiVideoUserImageChangesCursorDto GetCursorFromLastItem(List<PrdbApiVideoUserImageChangeDto> items)
    {
        var last = items[^1].VideoUserImage;
        return new PrdbApiVideoUserImageChangesCursorDto(last.UpdatedAtUtc, last.Id);
    }

    private static string BuildChangesUrl(DateTime? since, Guid? sinceId, int pageSize)
    {
        var effectiveSince = NormalizeUtc(since ?? InitialSinceUtc);
        var url = $"video-user-images/changes?PageSize={pageSize}&Since={Uri.EscapeDataString(effectiveSince.ToString("O"))}";
        if (sinceId.HasValue)
            url += $"&SinceId={sinceId.Value}";
        return url;
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
