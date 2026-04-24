using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb.Sync;

public class PrdbSyncService(AppDbContext db, IHttpClientFactory httpClientFactory, ILogger<PrdbSyncService> logger, PrdbVideoUserImageSyncService videoUserImageSync)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly DateTime InitialSinceUtc = DateTime.UnixEpoch;
    private const int PageSize = 100;
    private const int LatestVideosLimit = 1500;
    private const int ChangesPageSize = 1000;

    public async Task<PrdbSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("PrdbSyncService: PrdbApiKey is not configured — skipping sync");
            return new PrdbSyncResult();
        }

        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);

        var networksUpserted       = await SyncSitesAsync(http, ct);
        var sitesUpserted          = networksUpserted.sitesUpserted;
        var favSitesUpserted       = await SyncFavoriteSitesAsync(http, settings, ct);
        var favActorsUpserted      = await SyncFavoriteActorsAsync(http, settings, ct);
        var videosUpserted         = await SyncVideosAsync(http, ct);
        var videoUserImagesSynced  = await videoUserImageSync.RunAsync(ct);

        return new PrdbSyncResult
        {
            NetworksUpserted     = networksUpserted.networksUpserted,
            SitesUpserted        = sitesUpserted,
            FavoriteSitesSynced  = favSitesUpserted,
            FavoriteActorsSynced = favActorsUpserted,
            VideosUpserted       = videosUpserted,
            VideoUserImagesSynced = videoUserImagesSynced,
        };
    }

    // ── Sites + Networks ─────────────────────────────────────────────────────

    private async Task<(int networksUpserted, int sitesUpserted)> SyncSitesAsync(HttpClient http, CancellationToken ct)
    {
        logger.LogInformation("PrdbSyncService: syncing sites");

        var apiSites = await FetchAllPagesAsync<PrdbApiSite>(http, "sites", ct);

        // Upsert networks derived from site data
        var networksFromApi = apiSites
            .Where(s => s.NetworkId.HasValue && s.NetworkTitle != null)
            .GroupBy(s => s.NetworkId!.Value)
            .Select(g => g.First())
            .ToList();

        var existingNetworks = await db.PrdbNetworks
            .ToDictionaryAsync(n => n.Id, ct);

        var now = DateTime.UtcNow;

        foreach (var apiSite in networksFromApi)
        {
            if (existingNetworks.TryGetValue(apiSite.NetworkId!.Value, out var existing))
            {
                existing.Title       = apiSite.NetworkTitle!;
                existing.SyncedAtUtc = now;
            }
            else
            {
                var network = new PrdbNetwork
                {
                    Id          = apiSite.NetworkId!.Value,
                    Title       = apiSite.NetworkTitle!,
                    Url         = string.Empty,
                    SyncedAtUtc = now,
                };
                db.PrdbNetworks.Add(network);
                existingNetworks[network.Id] = network;
            }
        }

        await db.SaveChangesAsync(ct);
        var networksUpserted = networksFromApi.Count;

        // Upsert sites
        var existingSites = await db.PrdbSites
            .ToDictionaryAsync(s => s.Id, ct);

        foreach (var apiSite in apiSites)
        {
            if (existingSites.TryGetValue(apiSite.Id, out var existing))
            {
                existing.Title       = apiSite.Title;
                existing.Url         = apiSite.Url;
                existing.NetworkId   = apiSite.NetworkId;
                existing.SyncedAtUtc = now;
            }
            else
            {
                db.PrdbSites.Add(new PrdbSite
                {
                    Id          = apiSite.Id,
                    Title       = apiSite.Title,
                    Url         = apiSite.Url,
                    NetworkId   = apiSite.NetworkId,
                    SyncedAtUtc = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("PrdbSyncService: upserted {Networks} networks, {Sites} sites",
            networksUpserted, apiSites.Count);

        return (networksUpserted, apiSites.Count);
    }

    // ── Favorite Sites ───────────────────────────────────────────────────────

    private async Task<int> SyncFavoriteSitesAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        logger.LogInformation(
            "PrdbSyncService: syncing favorite sites from {Since} / {SinceId}",
            settings.PrdbFavoriteSiteSyncCursorUtc?.ToString("O") ?? "<start>",
            settings.PrdbFavoriteSiteSyncCursorId?.ToString() ?? "<none>");

        var totalApplied = 0;

        while (true)
        {
            var url = BuildChangesUrl("favorite-sites/changes", settings.PrdbFavoriteSiteSyncCursorUtc, settings.PrdbFavoriteSiteSyncCursorId, ChangesPageSize);
            logger.LogInformation(
                "PrdbSyncService: requesting favorite site changes from {Url}",
                BuildAbsoluteUrl(http, url));
            using var httpResponse = await http.GetAsync(url, ct);
            httpResponse.EnsureSuccessStatusCode();

            var responseDateUtc = httpResponse.Headers.Date?.UtcDateTime;
            var response = await httpResponse.Content.ReadFromJsonAsync<PrdbApiFavoriteSiteChangesResponse>(
                JsonOptions,
                ct);
            logger.LogInformation(
                "PrdbSyncService: received {Count} favorite site change(s), hasMore={HasMore}, nextCursor={NextCursorUpdatedAt} / {NextCursorId}",
                response?.Items.Count ?? 0,
                response?.HasMore ?? false,
                response?.NextCursor?.UpdatedAtUtc.ToString("O") ?? "<none>",
                response?.NextCursor?.Id.ToString() ?? "<none>");

            if (response is null || response.Items.Count == 0)
            {
                if (settings.PrdbFavoriteSiteSyncCursorUtc is null)
                {
                    settings.PrdbFavoriteSiteSyncCursorUtc = responseDateUtc ?? DateTime.UtcNow;
                    settings.PrdbFavoriteSiteSyncCursorId = null;
                    await db.SaveChangesAsync(ct);
                }

                break;
            }

            await ApplyFavoriteSiteChangesAsync(response.Items, ct);
            totalApplied += response.Items.Count;

            var cursor = response.NextCursor ?? GetCursorFromLastFavoriteSiteItem(response.Items);
            settings.PrdbFavoriteSiteSyncCursorUtc = cursor.UpdatedAtUtc;
            settings.PrdbFavoriteSiteSyncCursorId = cursor.Id;
            await db.SaveChangesAsync(ct);

            if (!response.HasMore)
                break;
        }

        logger.LogInformation("PrdbSyncService: {Count} favorite site changes applied", totalApplied);
        return totalApplied;
    }

    // ── Favorite Actors ──────────────────────────────────────────────────────

    private async Task<int> SyncFavoriteActorsAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        logger.LogInformation(
            "PrdbSyncService: syncing favorite actors from {Since} / {SinceId}",
            settings.PrdbFavoriteActorSyncCursorUtc?.ToString("O") ?? "<start>",
            settings.PrdbFavoriteActorSyncCursorId?.ToString() ?? "<none>");

        var totalApplied = 0;

        while (true)
        {
            var url = BuildChangesUrl("favorite-actors/changes", settings.PrdbFavoriteActorSyncCursorUtc, settings.PrdbFavoriteActorSyncCursorId, ChangesPageSize);
            logger.LogInformation(
                "PrdbSyncService: requesting favorite actor changes from {Url}",
                BuildAbsoluteUrl(http, url));
            using var httpResponse = await http.GetAsync(url, ct);
            httpResponse.EnsureSuccessStatusCode();

            var responseDateUtc = httpResponse.Headers.Date?.UtcDateTime;
            var response = await httpResponse.Content.ReadFromJsonAsync<PrdbApiFavoriteActorChangesResponse>(
                JsonOptions,
                ct);
            logger.LogInformation(
                "PrdbSyncService: received {Count} favorite actor change(s), hasMore={HasMore}, nextCursor={NextCursorUpdatedAt} / {NextCursorId}",
                response?.Items.Count ?? 0,
                response?.HasMore ?? false,
                response?.NextCursor?.UpdatedAtUtc.ToString("O") ?? "<none>",
                response?.NextCursor?.Id.ToString() ?? "<none>");

            if (response is null || response.Items.Count == 0)
            {
                if (settings.PrdbFavoriteActorSyncCursorUtc is null)
                {
                    settings.PrdbFavoriteActorSyncCursorUtc = responseDateUtc ?? DateTime.UtcNow;
                    settings.PrdbFavoriteActorSyncCursorId = null;
                    await db.SaveChangesAsync(ct);
                }

                break;
            }

            await ApplyFavoriteActorChangesAsync(response.Items, ct);
            totalApplied += response.Items.Count;

            var cursor = response.NextCursor ?? GetCursorFromLastFavoriteActorItem(response.Items);
            settings.PrdbFavoriteActorSyncCursorUtc = cursor.UpdatedAtUtc;
            settings.PrdbFavoriteActorSyncCursorId = cursor.Id;
            await db.SaveChangesAsync(ct);

            if (!response.HasMore)
                break;
        }

        logger.LogInformation("PrdbSyncService: {Count} favorite actor changes applied", totalApplied);
        return totalApplied;
    }

    // ── Videos ───────────────────────────────────────────────────────────────

    private async Task<int> SyncVideosAsync(HttpClient http, CancellationToken ct)
    {
        logger.LogInformation("PrdbSyncService: syncing videos");

        var favoriteSiteIds = await db.PrdbSites
            .Where(s => s.IsFavorite)
            .Select(s => s.Id)
            .ToListAsync(ct);

        // Collect all videos to upsert, deduplicated by ID
        var allApiVideos = new Dictionary<Guid, PrdbApiVideo>();

        // Latest 1500 global videos
        var latestVideos = await FetchAllPagesAsync<PrdbApiVideo>(http, "videos", ct, maxItems: LatestVideosLimit);
        foreach (var v in latestVideos)
            allApiVideos[v.Id] = v;

        // All videos for each favorite site
        foreach (var siteId in favoriteSiteIds)
        {
            var siteVideos = await FetchAllPagesAsync<PrdbApiVideo>(http, $"videos?SiteId={siteId}", ct);
            foreach (var v in siteVideos)
                allApiVideos[v.Id] = v;
        }

        // All videos for each favorite actor
        var favoriteActorIds = await db.PrdbActors
            .Where(a => a.IsFavorite)
            .Select(a => a.Id)
            .ToListAsync(ct);

        foreach (var actorId in favoriteActorIds)
        {
            var actorVideos = await FetchAllPagesAsync<PrdbApiVideo>(http, $"videos?ActorId={actorId}", ct);
            foreach (var v in actorVideos)
                allApiVideos[v.Id] = v;
        }

        return await UpsertVideosAsync(allApiVideos, ct);
    }

    public async Task SyncSiteVideosAsync(Guid siteId, CancellationToken ct = default)
    {
        var settings = await db.GetSettingsAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("PrdbSyncService: PrdbApiKey is not configured — skipping site video sync");
            return;
        }

        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);

        logger.LogInformation("PrdbSyncService: syncing videos for newly-favorited site {SiteId}", siteId);

        var siteVideos = await FetchAllPagesAsync<PrdbApiVideo>(http, $"videos?SiteId={siteId}", ct);
        if (siteVideos.Count == 0)
        {
            logger.LogInformation("PrdbSyncService: no videos found for site {SiteId}", siteId);
            return;
        }

        var allApiVideos = siteVideos.ToDictionary(v => v.Id);
        await UpsertVideosAsync(allApiVideos, ct);
    }

    private async Task<int> UpsertVideosAsync(Dictionary<Guid, PrdbApiVideo> allApiVideos, CancellationToken ct)
    {
        if (allApiVideos.Count == 0)
        {
            logger.LogInformation("PrdbSyncService: no videos to sync");
            return 0;
        }

        // Load existing video IDs to determine inserts vs updates
        var existingIds = await db.PrdbVideos
            .Where(v => allApiVideos.Keys.Contains(v.Id))
            .Select(v => v.Id)
            .ToHashSetAsync(ct);

        // Load known site IDs so we can skip videos whose site isn't synced
        var knownSiteIds = await db.PrdbSites
            .Select(s => s.Id)
            .ToHashSetAsync(ct);

        var now = DateTime.UtcNow;

        var toInsert = allApiVideos.Values
            .Where(v => !existingIds.Contains(v.Id) && knownSiteIds.Contains(v.SiteId))
            .Select(v => new PrdbVideo
            {
                Id               = v.Id,
                Title            = v.Title,
                ReleaseDate      = v.ReleaseDate,
                SiteId           = v.SiteId,
                PrdbCreatedAtUtc = now,
                PrdbUpdatedAtUtc = now,
                SyncedAtUtc      = now,
            })
            .ToList();

        var toUpdate = allApiVideos.Values
            .Where(v => existingIds.Contains(v.Id))
            .ToList();

        db.PrdbVideos.AddRange(toInsert);
        await db.SaveChangesAsync(ct);

        foreach (var batch in toUpdate.Chunk(200))
        {
            var ids = batch.Select(v => v.Id).ToList();
            var entities = await db.PrdbVideos
                .Where(v => ids.Contains(v.Id))
                .ToListAsync(ct);

            var lookup = batch.ToDictionary(v => v.Id);
            foreach (var entity in entities)
            {
                var api = lookup[entity.Id];
                entity.Title            = api.Title;
                entity.ReleaseDate      = api.ReleaseDate;
                entity.PrdbUpdatedAtUtc = now;
                entity.SyncedAtUtc      = now;
            }

            await db.SaveChangesAsync(ct);
        }

        var total = toInsert.Count + toUpdate.Count;
        logger.LogInformation("PrdbSyncService: upserted {Videos} videos ({Inserted} new, {Updated} updated)",
            total, toInsert.Count, toUpdate.Count);

        return total;
    }

    private async Task ApplyFavoriteSiteChangesAsync(List<PrdbApiFavoriteSiteChangeDto> items, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids = items.Select(i => i.FavoriteSite.Id).Distinct().ToList();

        var existingSites = await db.PrdbSites
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var incomingNetworks = items
            .Select(i => i.FavoriteSite)
            .Where(i => i.NetworkId.HasValue && !string.IsNullOrWhiteSpace(i.NetworkTitle))
            .GroupBy(i => i.NetworkId!.Value)
            .Select(g => g.Last())
            .ToList();

        if (incomingNetworks.Count > 0)
        {
            var networkIds = incomingNetworks.Select(n => n.NetworkId!.Value).ToList();
            var existingNetworks = await db.PrdbNetworks
                .Where(n => networkIds.Contains(n.Id))
                .ToDictionaryAsync(n => n.Id, ct);

            foreach (var network in incomingNetworks)
            {
                if (existingNetworks.TryGetValue(network.NetworkId!.Value, out var existing))
                {
                    existing.Title = network.NetworkTitle!;
                    existing.SyncedAtUtc = now;
                }
                else
                {
                    db.PrdbNetworks.Add(new PrdbNetwork
                    {
                        Id = network.NetworkId.Value,
                        Title = network.NetworkTitle!,
                        Url = string.Empty,
                        SyncedAtUtc = now,
                    });
                }
            }
        }

        foreach (var change in items)
        {
            var dto = change.FavoriteSite;

            if (!existingSites.TryGetValue(dto.Id, out var site))
            {
                site = new PrdbSite
                {
                    Id = dto.Id,
                    SyncedAtUtc = now,
                };
                db.PrdbSites.Add(site);
                existingSites[dto.Id] = site;
            }

            site.Title = dto.Title;
            site.Url = dto.Url;
            site.NetworkId = dto.NetworkId;
            site.SyncedAtUtc = now;

            if (dto.IsDeleted)
            {
                site.IsFavorite = false;
                site.FavoritedAtUtc = null;
            }
            else
            {
                site.IsFavorite = true;
                site.FavoritedAtUtc = dto.FavoritedAtUtc;
            }
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    private async Task ApplyFavoriteActorChangesAsync(List<PrdbApiFavoriteActorChangeDto> items, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids = items.Select(i => i.FavoriteActor.Id).Distinct().ToList();

        var existingActors = await db.PrdbActors
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        foreach (var change in items)
        {
            var dto = change.FavoriteActor;

            if (!existingActors.TryGetValue(dto.Id, out var actor))
            {
                actor = new PrdbActor
                {
                    Id = dto.Id,
                    Name = dto.Name,
                    Gender = 0,
                    PrdbCreatedAtUtc = now,
                    PrdbUpdatedAtUtc = now,
                    SyncedAtUtc = now,
                };
                db.PrdbActors.Add(actor);
                existingActors[dto.Id] = actor;
            }

            actor.Name = dto.Name;
            actor.PrdbUpdatedAtUtc = now;
            actor.SyncedAtUtc = now;

            if (dto.IsDeleted)
            {
                actor.IsFavorite = false;
                actor.FavoritedAtUtc = null;
            }
            else
            {
                actor.IsFavorite = true;
                actor.FavoritedAtUtc = dto.FavoritedAtUtc;
            }
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    // ── Pagination helper ────────────────────────────────────────────────────

    private async Task<List<T>> FetchAllPagesAsync<T>(
        HttpClient http,
        string endpoint,
        CancellationToken ct,
        int? maxItems = null)
    {
        var results = new List<T>();
        var page = 1;
        var separator = endpoint.Contains('?') ? '&' : '?';

        while (true)
        {
            var url = $"{endpoint}{separator}Page={page}&PageSize={PageSize}";
            var response = await http.GetFromJsonAsync<PrdbApiPagedResult<T>>(url, JsonOptions, ct);

            if (response is null || response.Items.Count == 0) break;

            results.AddRange(response.Items);

            if (maxItems.HasValue && results.Count >= maxItems.Value)
            {
                results = results.Take(maxItems.Value).ToList();
                break;
            }

            if (results.Count >= response.TotalCount) break;

            page++;
        }

        return results;
    }

    private static string BuildChangesUrl(string endpoint, DateTime? since, Guid? sinceId, int pageSize)
    {
        var effectiveSince = NormalizeUtc(since ?? InitialSinceUtc);
        var url = $"{endpoint}?PageSize={pageSize}&Since={Uri.EscapeDataString(effectiveSince.ToString("O"))}";

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

    private static PrdbApiFavoriteSiteChangesCursorDto GetCursorFromLastFavoriteSiteItem(List<PrdbApiFavoriteSiteChangeDto> items)
    {
        var last = items[^1].FavoriteSite;
        return new PrdbApiFavoriteSiteChangesCursorDto(last.UpdatedAtUtc, last.Id);
    }

    private static PrdbApiFavoriteActorChangesCursorDto GetCursorFromLastFavoriteActorItem(List<PrdbApiFavoriteActorChangeDto> items)
    {
        var last = items[^1].FavoriteActor;
        return new PrdbApiFavoriteActorChangesCursorDto(last.UpdatedAtUtc, last.Id);
    }
}
