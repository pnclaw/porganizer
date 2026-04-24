using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb.Sync;

public class AutoWantedVideoSyncService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<AutoWantedVideoSyncService> logger)
{
    private const int MaxDaysBack = 14;

    public async Task RunAsync(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (!settings.AutoAddAllNewVideos)
        {
            logger.LogDebug("AutoWantedVideoSyncService: disabled — skipping");
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("AutoWantedVideoSyncService: PrdbApiKey not configured — skipping");
            return;
        }

        var daysBack = Math.Min(settings.AutoAddAllNewVideosDaysBack, MaxDaysBack);
        var cutoff   = DateTime.UtcNow.AddDays(-daysBack);

        // Videos added to prdb.net within the window that have at least one indexer match.
        var candidateIds = await db.PrdbVideos
            .Where(v => v.PrdbCreatedAtUtc >= cutoff
                && db.IndexerRowMatches.Any(m => m.PrdbVideoId == v.Id))
            .Select(v => v.Id)
            .ToListAsync(ct);

        if (candidateIds.Count == 0)
        {
            logger.LogInformation("AutoWantedVideoSyncService: no candidate videos found");
            settings.AutoAddAllNewVideosLastRunAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        var alreadyWanted = await db.PrdbWantedVideos
            .Where(w => candidateIds.Contains(w.VideoId))
            .Select(w => w.VideoId)
            .ToHashSetAsync(ct);

        var alreadyInLibrary = await db.LibraryFiles
            .Where(f => f.VideoId != null && candidateIds.Contains(f.VideoId.Value))
            .Select(f => f.VideoId!.Value)
            .ToHashSetAsync(ct);

        var toAdd = candidateIds.Where(id => !alreadyWanted.Contains(id) && !alreadyInLibrary.Contains(id)).ToList();

        logger.LogInformation(
            "AutoWantedVideoSyncService: {Candidates} candidates, {AlreadyWanted} already wanted, {AlreadyInLibrary} already in library, {ToAdd} to add",
            candidateIds.Count, alreadyWanted.Count, alreadyInLibrary.Count, toAdd.Count);

        if (toAdd.Count == 0)
        {
            settings.AutoAddAllNewVideosLastRunAt = DateTime.UtcNow;
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
                        "AutoWantedVideoSyncService: video {VideoId} not found on prdb.net — skipping",
                        videoId);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                db.PrdbWantedVideos.Add(new PrdbWantedVideo
                {
                    VideoId             = videoId,
                    IsFulfilled         = false,
                    FulfillAllQualities = settings.AutoAddAllNewVideosFulfillAllQualities,
                    PrdbCreatedAtUtc    = now,
                    PrdbUpdatedAtUtc    = now,
                    SyncedAtUtc         = now,
                });

                added++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "AutoWantedVideoSyncService: error adding video {VideoId} to wanted list",
                    videoId);
            }
        }

        settings.AutoAddAllNewVideosLastRunAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("AutoWantedVideoSyncService: added {Added} video(s) to wanted list", added);
    }

    private HttpClient CreateClient(AppSettings settings)
    {
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);
        return http;
    }
}
