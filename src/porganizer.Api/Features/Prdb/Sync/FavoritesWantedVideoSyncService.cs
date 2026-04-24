using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb.Sync;

public class FavoritesWantedVideoSyncService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<FavoritesWantedVideoSyncService> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (!settings.FavoritesWantedEnabled)
        {
            logger.LogDebug("FavoritesWantedVideoSyncService: disabled — skipping");
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("FavoritesWantedVideoSyncService: PrdbApiKey not configured — skipping");
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-settings.FavoritesWantedDaysBack);

        // Find all videos added to prdb.net within the window that belong to a favorite site
        // or have at least one favorite actor.
        var candidateIds = await db.PrdbVideos
            .Where(v => v.PrdbCreatedAtUtc >= cutoff
                && (v.Site.IsFavorite || v.VideoActors.Any(va => va.Actor.IsFavorite)))
            .Select(v => v.Id)
            .ToListAsync(ct);

        if (candidateIds.Count == 0)
        {
            logger.LogInformation("FavoritesWantedVideoSyncService: no candidate videos found");
            settings.FavoritesWantedLastRunAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Exclude videos already on the wanted list.
        var alreadyWanted = await db.PrdbWantedVideos
            .Where(w => candidateIds.Contains(w.VideoId))
            .Select(w => w.VideoId)
            .ToHashSetAsync(ct);

        var toAdd = candidateIds.Where(id => !alreadyWanted.Contains(id)).ToList();

        logger.LogInformation(
            "FavoritesWantedVideoSyncService: {Candidates} candidates, {AlreadyWanted} already wanted, {ToAdd} to add",
            candidateIds.Count, alreadyWanted.Count, toAdd.Count);

        if (toAdd.Count == 0)
        {
            settings.FavoritesWantedLastRunAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        var http = CreateClient(settings);
        var now  = DateTime.UtcNow;
        var added = 0;

        foreach (var videoId in toAdd)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var response = await http.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, $"wanted-videos/{videoId}"), ct);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    logger.LogWarning(
                        "FavoritesWantedVideoSyncService: video {VideoId} not found on prdb.net — skipping",
                        videoId);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                db.PrdbWantedVideos.Add(new PrdbWantedVideo
                {
                    VideoId          = videoId,
                    IsFulfilled      = false,
                    PrdbCreatedAtUtc = now,
                    PrdbUpdatedAtUtc = now,
                    SyncedAtUtc      = now,
                });

                added++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "FavoritesWantedVideoSyncService: error adding video {VideoId} to wanted list",
                    videoId);
            }
        }

        settings.FavoritesWantedLastRunAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("FavoritesWantedVideoSyncService: added {Added} video(s) to wanted list", added);
    }

    private HttpClient CreateClient(AppSettings settings)
    {
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);
        return http;
    }
}
