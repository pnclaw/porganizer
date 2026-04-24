using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.Prdb.Sync;

public class PrdbIndexerFilehashSyncService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<PrdbIndexerFilehashSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const int BackfillPageSize  = 1000;
    private const int ChangesPageSize   = 1000;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("PrdbIndexerFilehashSyncService: PrdbApiKey not configured - skipping");
            return;
        }

        var http = CreateClient(settings);

        if (settings.PrdbIndexerFilehashBackfillPage is not null || settings.PrdbIndexerFilehashSyncCursorUtc is null)
            await RunBackfillAsync(http, settings, ct);
        else
            await RunIncrementalAsync(http, settings, ct);
    }

    private async Task RunBackfillAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        var currentPage   = settings.PrdbIndexerFilehashBackfillPage ?? 1;
        var startPage     = currentPage;
        var totalUpserted = 0;
        var backfillStartedAt = DateTime.UtcNow;

        logger.LogInformation("PrdbIndexerFilehashSyncService: backfill starting at page {Page}", startPage);

        var url      = $"indexer-filehashes/latest?Page={currentPage}&PageSize={BackfillPageSize}&SortDirection=asc";
        var response = await http.GetFromJsonAsync<PrdbApiPagedResult<PrdbApiIndexerFilehashDto>>(
            url, JsonOptions, ct);

        bool done;
        if (response is null || response.Items.Count == 0)
        {
            done = true;
        }
        else
        {
            await UpsertFilehashesAsync(response.Items, ct);
            totalUpserted += response.Items.Count;
            settings.PrdbIndexerFilehashBackfillTotalCount = response.TotalCount;

            var fetched = (long)currentPage * BackfillPageSize;
            currentPage++;
            done = fetched >= response.TotalCount;
        }

        settings.PrdbIndexerFilehashBackfillPage  = done ? null : currentPage;
        settings.PrdbIndexerFilehashSyncCursorUtc = done ? backfillStartedAt.AddMinutes(-5) : settings.PrdbIndexerFilehashSyncCursorUtc;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PrdbIndexerFilehashSyncService: backfill page {Page} - upserted {Upserted}, next: {Next}",
            startPage, totalUpserted,
            settings.PrdbIndexerFilehashBackfillPage?.ToString() ?? "done");
    }

    private async Task RunIncrementalAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        var since = settings.PrdbIndexerFilehashSyncCursorUtc;
        if (since is null)
        {
            logger.LogWarning("PrdbIndexerFilehashSyncService: incremental sync requested without a cursor - skipping");
            return;
        }

        logger.LogInformation(
            "PrdbIndexerFilehashSyncService: incremental change sync from {Since:O} / {SinceId}",
            since.Value,
            settings.PrdbIndexerFilehashSyncCursorId);

        while (true)
        {
            var url = BuildChangesUrl(since.Value, settings.PrdbIndexerFilehashSyncCursorId, ChangesPageSize);
            var response = await http.GetFromJsonAsync<PrdbApiIndexerFilehashChangesResponse>(url, JsonOptions, ct);

            if (response is null || response.Items.Count == 0)
                break;

            await ApplyChangesAsync(response.Items, ct);

            var cursor = response.NextCursor ?? GetCursorFromLastItem(response.Items);
            settings.PrdbIndexerFilehashSyncCursorUtc = cursor.UpdatedAtUtc;
            settings.PrdbIndexerFilehashSyncCursorId  = cursor.Id;
            await db.SaveChangesAsync(ct);
            since = cursor.UpdatedAtUtc;

            if (!response.HasMore)
                break;
        }

        logger.LogInformation(
            "PrdbIndexerFilehashSyncService: incremental change sync complete at {Since:O} / {SinceId}",
            settings.PrdbIndexerFilehashSyncCursorUtc,
            settings.PrdbIndexerFilehashSyncCursorId);
    }

    private async Task UpsertFilehashesAsync(List<PrdbApiIndexerFilehashDto> items, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids      = items.Select(i => i.Id).ToList();
        var existing = await db.PrdbIndexerFilehashes
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        foreach (var item in items)
        {
            if (existing.TryGetValue(item.Id, out var entity))
            {
                MapFilehash(entity, item, now);
                continue;
            }

            entity = new PrdbIndexerFilehash();
            MapFilehash(entity, item, now);
            db.PrdbIndexerFilehashes.Add(entity);
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    private async Task ApplyChangesAsync(List<PrdbApiIndexerFilehashChangeDto> items, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids = items.Select(i => i.Filehash.Id).Distinct().ToList();

        var existing = await db.PrdbIndexerFilehashes
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        foreach (var item in items)
        {
            var dto = item.Filehash;

            if (existing.TryGetValue(dto.Id, out var entity))
            {
                MapFilehash(entity, dto, now);
                continue;
            }

            entity = new PrdbIndexerFilehash();
            MapFilehash(entity, dto, now);
            db.PrdbIndexerFilehashes.Add(entity);
            existing[dto.Id] = entity;
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    private static void MapFilehash(PrdbIndexerFilehash entity, PrdbApiIndexerFilehashDto dto, DateTime now)
    {
        entity.Id              = dto.Id;
        entity.IndexerSource   = MapIndexerSource(dto.IndexerSource);
        entity.IndexerId       = dto.IndexerId;
        entity.Filename        = dto.Filename;
        entity.OsHash          = dto.OsHash;
        entity.PHash           = dto.PHash;
        entity.Filesize        = dto.Filesize;
        entity.SubmissionCount = dto.SubmissionCount;
        entity.IsVerified      = dto.IsVerified;
        entity.IsDeleted       = false;
        entity.DeletedAtUtc    = null;
        entity.PrdbCreatedAtUtc = dto.CreatedAtUtc;
        entity.PrdbUpdatedAtUtc = dto.UpdatedAtUtc;
        entity.SyncedAtUtc      = now;
    }

    private static void MapFilehash(PrdbIndexerFilehash entity, PrdbApiIndexerFilehashChangeFilehashDto dto, DateTime now)
    {
        entity.Id              = dto.Id;
        entity.IndexerSource   = MapIndexerSource(dto.IndexerSource);
        entity.IndexerId       = dto.IndexerId;
        entity.Filename        = dto.Filename;
        entity.OsHash          = dto.OsHash;
        entity.PHash           = dto.PHash;
        entity.Filesize        = dto.Filesize;
        entity.SubmissionCount = dto.SubmissionCount;
        entity.IsVerified      = dto.IsVerified;
        entity.IsDeleted       = dto.IsDeleted;
        entity.DeletedAtUtc    = dto.DeletedAtUtc;
        entity.PrdbCreatedAtUtc = dto.CreatedAtUtc;
        entity.PrdbUpdatedAtUtc = dto.UpdatedAtUtc;
        entity.SyncedAtUtc      = now;
    }

    private static int MapIndexerSource(string value) =>
        value.ToLowerInvariant() switch
        {
            "drunkenslug" => (int)IndexerSource.DrunkenSlug,
            "nzbfinder"   => (int)IndexerSource.NzbFinder,
            _             => throw new InvalidOperationException($"Unknown IndexerSource: '{value}'"),
        };

    private static PrdbApiIndexerFilehashChangesCursorDto GetCursorFromLastItem(List<PrdbApiIndexerFilehashChangeDto> items)
    {
        var last = items[^1].Filehash;
        return new PrdbApiIndexerFilehashChangesCursorDto(last.UpdatedAtUtc, last.Id);
    }

    private static string BuildChangesUrl(DateTime since, Guid? sinceId, int pageSize)
    {
        since = NormalizeUtc(since);
        var url = $"indexer-filehashes/changes?Since={Uri.EscapeDataString(since.ToString("O"))}&PageSize={pageSize}";
        if (sinceId.HasValue)
            url += $"&SinceId={sinceId.Value}";
        return url;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc   => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private HttpClient CreateClient(AppSettings settings)
    {
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);
        return http;
    }
}
