using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb.Sync;

public class PrdbLatestPreDbSyncService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<PrdbLatestPreDbSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const int PageSize    = 500;
    private const int PagesPerRun = 10; // 5,000 PreDb entries per run, 10 API requests

    public async Task RunAsync(CancellationToken ct = default)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("PrdbLatestPreDbSyncService: PrdbApiKey not configured - skipping");
            return;
        }

        var http = CreateClient(settings);

        if (settings.PrenamesBackfillPage is not null || settings.PrenamesSyncCursorUtc is null)
            await RunBackfillAsync(http, settings, ct);
        else
            await RunIncrementalAsync(http, settings, ct);
    }

    private async Task RunBackfillAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        var startPage          = settings.PrenamesBackfillPage ?? 1;
        var currentPage        = startPage;
        var totalUpserted      = 0;
        var totalUnlinked      = 0;
        var done               = false;

        logger.LogInformation("PrdbLatestPreDbSyncService: backfill starting at page {Page}", startPage);

        for (var i = 0; i < PagesPerRun; i++)
        {
            var url      = $"predb/latest?Page={currentPage}&PageSize={PageSize}";
            var response = await http.GetFromJsonAsync<PrdbApiPagedResult<PrdbApiLatestPreDbItem>>(
                url, JsonOptions, ct);

            if (response is null || response.Items.Count == 0)
            {
                done = true;
                break;
            }

            var result = await UpsertPreDbEntriesAsync(response.Items, ct);
            totalUpserted  += result.UpsertedEntries;
            totalUnlinked  += result.UnlinkedEntries;

            settings.PrenamesBackfillTotalCount = response.TotalCount;

            var fetched = (long)currentPage * PageSize;
            currentPage++;

            if (fetched >= response.TotalCount)
            {
                done = true;
                break;
            }
        }

        settings.PrenamesBackfillPage  = done ? null : currentPage;
        settings.PrenamesSyncCursorUtc = done ? DateTime.UtcNow : settings.PrenamesSyncCursorUtc;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PrdbLatestPreDbSyncService: backfill pages {Start}-{End} - upserted {Upserted}, skipped {Unlinked} unlinked, next: {Next}",
            startPage, currentPage - 1, totalUpserted, totalUnlinked,
            settings.PrenamesBackfillPage?.ToString() ?? "done");
    }

    private async Task RunIncrementalAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        var cursor       = settings.PrenamesSyncCursorUtc!.Value;
        var runStartedAt = DateTime.UtcNow;
        var fetchFrom    = cursor.AddDays(-7);

        logger.LogInformation("PrdbLatestPreDbSyncService: incremental sync since {Cursor:O} (re-fetching 7-day window from {FetchFrom:O})", cursor, fetchFrom);

        var allItems    = new List<PrdbApiLatestPreDbItem>();
        var page        = 1;
        var cursorParam = Uri.EscapeDataString(fetchFrom.ToString("O"));

        while (true)
        {
            var url      = $"predb/latest?Page={page}&PageSize={PageSize}&CreatedFrom={cursorParam}";
            var response = await http.GetFromJsonAsync<PrdbApiPagedResult<PrdbApiLatestPreDbItem>>(
                url, JsonOptions, ct);

            if (response is null || response.Items.Count == 0)
                break;

            allItems.AddRange(response.Items);
            if (allItems.Count >= response.TotalCount)
                break;

            page++;
        }

        var result = allItems.Count > 0
            ? await UpsertPreDbEntriesAsync(allItems, ct)
            : new UpsertPreDbResult(0, 0);

        settings.PrenamesSyncCursorUtc = runStartedAt;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PrdbLatestPreDbSyncService: incremental sync complete - {Found} found, {Upserted} upserted, {Unlinked} unlinked",
            allItems.Count, result.UpsertedEntries, result.UnlinkedEntries);
    }

    private async Task<UpsertPreDbResult> UpsertPreDbEntriesAsync(List<PrdbApiLatestPreDbItem> items, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var linkedItems = items.Where(i => i.Video is not null).ToList();

        await UpsertSiteStubsAsync(linkedItems, now, ct);
        await UpsertVideoStubsAsync(linkedItems, now, ct);

        var upsertedEntries = await UpsertRawPreDbEntriesAsync(items, now, ct);
        var unlinked        = items.Count - linkedItems.Count;

        return new UpsertPreDbResult(upsertedEntries, unlinked);
    }

    private async Task UpsertSiteStubsAsync(List<PrdbApiLatestPreDbItem> items, DateTime now, CancellationToken ct)
    {
        var siteItems = items
            .Select(i => i.Video!.Site)
            .DistinctBy(s => s.Id)
            .ToDictionary(s => s.Id);

        if (siteItems.Count == 0)
            return;

        var existingSiteIds = await db.PrdbSites
            .Where(s => siteItems.Keys.Contains(s.Id))
            .Select(s => s.Id)
            .ToHashSetAsync(ct);

        foreach (var site in siteItems.Values.Where(s => !existingSiteIds.Contains(s.Id)))
        {
            db.PrdbSites.Add(new PrdbSite
            {
                Id          = site.Id,
                Title       = site.Title,
                Url         = string.Empty,
                SyncedAtUtc = now,
            });
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    private async Task UpsertVideoStubsAsync(List<PrdbApiLatestPreDbItem> items, DateTime now, CancellationToken ct)
    {
        var videoItems = items
            .Select(i => i.Video!)
            .DistinctBy(v => v.Id)
            .ToDictionary(v => v.Id);

        if (videoItems.Count == 0)
            return;

        var existingVideos = await db.PrdbVideos
            .Where(v => videoItems.Keys.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        foreach (var video in videoItems.Values)
        {
            if (existingVideos.TryGetValue(video.Id, out var existing))
            {
                existing.Title       = video.Title;
                existing.ReleaseDate = video.ReleaseDate;
                existing.SiteId      = video.Site.Id;
                existing.SyncedAtUtc = now;
                continue;
            }

            db.PrdbVideos.Add(new PrdbVideo
            {
                Id               = video.Id,
                Title            = video.Title,
                ReleaseDate      = video.ReleaseDate,
                SiteId           = video.Site.Id,
                PrdbCreatedAtUtc = now,
                PrdbUpdatedAtUtc = now,
                SyncedAtUtc      = now,
            });
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertRawPreDbEntriesAsync(List<PrdbApiLatestPreDbItem> items, DateTime now, CancellationToken ct)
    {
        var ids = items.Select(i => i.Id).ToList();
        var existingEntries = await db.PrdbPreDbEntries
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        foreach (var item in items)
        {
            if (existingEntries.TryGetValue(item.Id, out var existing))
            {
                MapPreDbEntry(existing, item, now);
                continue;
            }

            var entry = new PrdbPreDbEntry();
            MapPreDbEntry(entry, item, now);
            db.PrdbPreDbEntries.Add(entry);
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);

        return items.Count;
    }

    private static void MapPreDbEntry(PrdbPreDbEntry entry, PrdbApiLatestPreDbItem item, DateTime now)
    {
        entry.Id           = item.Id;
        entry.Title        = item.Title;
        entry.CreatedAtUtc = item.CreatedAtUtc;
        entry.PrdbVideoId  = item.Video?.Id;
        entry.PrdbSiteId   = item.Video?.Site.Id;
        entry.VideoTitle   = item.Video?.Title;
        entry.SiteTitle    = item.Video?.Site.Title;
        entry.ReleaseDate  = item.Video?.ReleaseDate;
        entry.SyncedAtUtc  = now;
    }

    private HttpClient CreateClient(AppSettings settings)
    {
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);
        return http;
    }

    private sealed record UpsertPreDbResult(int UpsertedEntries, int UnlinkedEntries);
}
