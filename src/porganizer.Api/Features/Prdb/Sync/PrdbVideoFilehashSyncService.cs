using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb.Sync;

public class PrdbVideoFilehashSyncService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<PrdbVideoFilehashSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const int PageSize    = 100;
    private const int ChangesPageSize = 1000;
    private const int PagesPerRun = 10; // 1,000 filehashes per run, 10 API requests

    public async Task RunAsync(CancellationToken ct = default)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("PrdbVideoFilehashSyncService: PrdbApiKey not configured - skipping");
            return;
        }

        var http = CreateClient(settings);

        if (settings.PrdbFilehashBackfillPage is not null || settings.PrdbFilehashSyncCursorUtc is null)
            await RunBackfillAsync(http, settings, ct);
        else
            await RunIncrementalAsync(http, settings, ct);
    }

    private async Task RunBackfillAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        var startPage     = settings.PrdbFilehashBackfillPage ?? 1;
        var currentPage   = startPage;
        var totalUpserted = 0;
        var done          = false;
        var backfillStartedAt = DateTime.UtcNow;

        logger.LogInformation("PrdbVideoFilehashSyncService: backfill starting at page {Page}", startPage);

        for (var i = 0; i < PagesPerRun; i++)
        {
            var url      = $"videos/filehashes/latest?Page={currentPage}&PageSize={PageSize}&SortDirection=asc";
            var response = await http.GetFromJsonAsync<PrdbApiPagedResult<PrdbApiVideoFilehashDto>>(
                url, JsonOptions, ct);

            if (response is null || response.Items.Count == 0)
            {
                done = true;
                break;
            }

            await UpsertFilehashesAsync(response.Items, ct);
            totalUpserted += response.Items.Count;

            settings.PrdbFilehashBackfillTotalCount = response.TotalCount;

            var fetched = (long)currentPage * PageSize;
            currentPage++;

            if (fetched >= response.TotalCount)
            {
                done = true;
                break;
            }
        }

        settings.PrdbFilehashBackfillPage    = done ? null : currentPage;
        // Use a point before the backfill started so the incremental feed re-checks
        // any records that were updated during the (potentially multi-day) backfill window.
        settings.PrdbFilehashSyncCursorUtc   = done ? backfillStartedAt.AddMinutes(-5) : settings.PrdbFilehashSyncCursorUtc;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PrdbVideoFilehashSyncService: backfill pages {Start}-{End} - upserted {Upserted}, next: {Next}",
            startPage, currentPage - 1, totalUpserted,
            settings.PrdbFilehashBackfillPage?.ToString() ?? "done");
    }

    private async Task RunIncrementalAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        var since = settings.PrdbFilehashSyncCursorUtc;
        if (since is null)
        {
            logger.LogWarning("PrdbVideoFilehashSyncService: incremental sync requested without a cursor - skipping");
            return;
        }

        logger.LogInformation(
            "PrdbVideoFilehashSyncService: incremental change sync from {Since:O} / {SinceId}",
            since.Value,
            settings.PrdbFilehashSyncCursorId);

        while (true)
        {
            PrdbApiVideoFilehashChangesResponse? response;
            try
            {
                var url = BuildChangesUrl(
                    since.Value,
                    settings.PrdbFilehashSyncCursorId,
                    ChangesPageSize);

                response = await http.GetFromJsonAsync<PrdbApiVideoFilehashChangesResponse>(
                    url, JsonOptions, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                await RunIncrementalLatestFallbackAsync(http, settings, since.Value, ct);
                return;
            }

            if (response is null || response.Items.Count == 0)
                break;

            await ApplyChangesAsync(response.Items, ct);

            var cursor = response.NextCursor ?? GetCursorFromLastItem(response.Items);
            settings.PrdbFilehashSyncCursorUtc = cursor.UpdatedAtUtc;
            settings.PrdbFilehashSyncCursorId = cursor.Id;
            await db.SaveChangesAsync(ct);
            since = cursor.UpdatedAtUtc;

            if (!response.HasMore)
                break;
        }

        logger.LogInformation(
            "PrdbVideoFilehashSyncService: incremental change sync complete at {Since:O} / {SinceId}",
            settings.PrdbFilehashSyncCursorUtc,
            settings.PrdbFilehashSyncCursorId);
    }

    private async Task RunIncrementalLatestFallbackAsync(
        HttpClient http,
        AppSettings settings,
        DateTime since,
        CancellationToken ct)
    {
        logger.LogWarning(
            "PrdbVideoFilehashSyncService: change feed endpoint not available, falling back to latest filehash sync without delete support");

        var page = 1;
        var totalUpserted = 0;
        var updatedFrom = Uri.EscapeDataString(since.ToString("O"));

        while (true)
        {
            var url = $"videos/filehashes/latest?Page={page}&PageSize={PageSize}&UpdatedFrom={updatedFrom}";
            var response = await http.GetFromJsonAsync<PrdbApiPagedResult<PrdbApiVideoFilehashDto>>(
                url, JsonOptions, ct);

            if (response is null || response.Items.Count == 0)
                break;

            await UpsertFilehashesAsync(response.Items, ct);
            totalUpserted += response.Items.Count;

            var fetched = page * response.PageSize;
            if (fetched >= response.TotalCount)
                break;

            page++;
        }

        settings.PrdbFilehashSyncCursorUtc = DateTime.UtcNow;
        settings.PrdbFilehashSyncCursorId = null;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PrdbVideoFilehashSyncService: fallback incremental sync complete - {Count} filehashes upserted",
            totalUpserted);
    }

    private async Task UpsertFilehashesAsync(List<PrdbApiVideoFilehashDto> items, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids      = items.Select(i => i.Id).ToList();
        var existing = await db.PrdbVideoFilehashes
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        foreach (var item in items)
        {
            if (existing.TryGetValue(item.Id, out var entity))
            {
                MapFilehash(entity, item, now);
                continue;
            }

            entity = new PrdbVideoFilehash();
            MapFilehash(entity, item, now);
            db.PrdbVideoFilehashes.Add(entity);
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    private async Task ApplyChangesAsync(List<PrdbApiVideoFilehashChangeDto> items, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids = items.Select(i => i.Filehash.Id).Distinct().ToList();

        var existing = await db.PrdbVideoFilehashes
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        foreach (var item in items)
        {
            var dto = item.Filehash;

            if (dto.IsDeleted)
            {
                if (existing.TryGetValue(dto.Id, out var toDelete))
                {
                    db.PrdbVideoFilehashes.Remove(toDelete);
                    existing.Remove(dto.Id);
                }

                continue;
            }

            if (existing.TryGetValue(dto.Id, out var entity))
            {
                MapFilehash(entity, dto, now);
                continue;
            }

            entity = new PrdbVideoFilehash();
            MapFilehash(entity, dto, now);
            db.PrdbVideoFilehashes.Add(entity);
            existing[dto.Id] = entity;
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    private static void MapFilehash(PrdbVideoFilehash entity, PrdbApiVideoFilehashDto dto, DateTime now)
    {
        entity.Id               = dto.Id;
        entity.VideoId          = dto.VideoId;
        entity.Filename         = dto.Filename;
        entity.OsHash           = dto.OsHash;
        entity.PHash            = dto.PHash;
        entity.Filesize         = dto.Filesize;
        entity.SubmissionCount  = dto.SubmissionCount;
        entity.IsVerified       = dto.IsVerified;
        entity.PrdbCreatedAtUtc = dto.CreatedAtUtc;
        entity.PrdbUpdatedAtUtc = dto.UpdatedAtUtc;
        entity.SyncedAtUtc      = now;
    }

    private static void MapFilehash(PrdbVideoFilehash entity, PrdbApiVideoFilehashChangeFilehashDto dto, DateTime now)
    {
        entity.Id               = dto.Id;
        entity.VideoId          = dto.VideoId;
        entity.Filename         = dto.Filename;
        entity.OsHash           = dto.OsHash;
        entity.PHash            = dto.PHash;
        entity.Filesize         = dto.Filesize;
        entity.SubmissionCount  = dto.SubmissionCount;
        entity.IsVerified       = dto.IsVerified;
        entity.PrdbCreatedAtUtc = dto.CreatedAtUtc;
        entity.PrdbUpdatedAtUtc = dto.UpdatedAtUtc;
        entity.SyncedAtUtc      = now;
    }

    private static PrdbApiVideoFilehashChangesCursorDto GetCursorFromLastItem(List<PrdbApiVideoFilehashChangeDto> items)
    {
        var last = items[^1].Filehash;
        return new PrdbApiVideoFilehashChangesCursorDto(last.UpdatedAtUtc, last.Id);
    }

    private static string BuildChangesUrl(DateTime since, Guid? sinceId, int pageSize)
    {
        since = NormalizeUtc(since);
        var url = $"videos/filehashes/changes?Since={Uri.EscapeDataString(since.ToString("O"))}&PageSize={pageSize}";
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

    private HttpClient CreateClient(AppSettings settings)
    {
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);
        return http;
    }
}
