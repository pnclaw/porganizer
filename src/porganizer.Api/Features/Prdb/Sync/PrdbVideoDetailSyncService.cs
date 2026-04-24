using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb.Sync;

public class PrdbVideoDetailSyncService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<PrdbVideoDetailSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const int DetailResyncDays        = 30;  // re-sync video details after this many days
    private const int RecentResyncHours       = 3;   // re-sync recently-added videos more frequently
    private const int RecentVideoAgeDays      = 3;   // videos younger than this use the fast resync interval
    private const int MaxVideoAgeDays         = 360; // videos older than this are not re-synced
    private const int VideoBatchSize          = 50;  // max IDs per /videos/batch request
    private const int VideoBatchesPerRun      = 20;  // 1 000 videos per run, 20 API requests
    private const int ActorBatchSize     = 50;
    private const int ActorBatchesPerRun = 20; // 1 000 actors per run, 20 API requests

    public async Task RunAsync(CancellationToken ct = default)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("PrdbVideoDetailSyncService: PrdbApiKey not configured — skipping");
            return;
        }

        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);

        await SyncVideoDetailsAsync(http, ct);
        await SyncActorDetailsAsync(http, ct);
    }

    // ── Phase 1: Video detail sync ───────────────────────────────────────────

    private async Task SyncVideoDetailsAsync(HttpClient http, CancellationToken ct)
    {
        var runAt              = DateTime.UtcNow;
        var ageCutoff          = runAt.AddDays(-MaxVideoAgeDays);
        var recentCutoff       = runAt.AddDays(-RecentVideoAgeDays);
        var recentResyncBefore = runAt.AddHours(-RecentResyncHours);
        var resyncBefore       = runAt.AddDays(-DetailResyncDays);

        var totalPending = await db.PrdbVideos
            .CountAsync(v =>
                v.PrdbCreatedAtUtc >= ageCutoff &&
                (
                    (v.PrdbCreatedAtUtc >= recentCutoff && (v.DetailSyncedAtUtc == null || v.DetailSyncedAtUtc < recentResyncBefore)) ||
                    (v.PrdbCreatedAtUtc <  recentCutoff && (v.DetailSyncedAtUtc == null || v.DetailSyncedAtUtc < resyncBefore))
                ), ct);

        if (totalPending == 0)
        {
            logger.LogInformation("PrdbVideoDetailSyncService: no videos pending detail sync");
            return;
        }

        var videoIds = await db.PrdbVideos
            .Where(v =>
                v.PrdbCreatedAtUtc >= ageCutoff &&
                (
                    (v.PrdbCreatedAtUtc >= recentCutoff && (v.DetailSyncedAtUtc == null || v.DetailSyncedAtUtc < recentResyncBefore)) ||
                    (v.PrdbCreatedAtUtc <  recentCutoff && (v.DetailSyncedAtUtc == null || v.DetailSyncedAtUtc < resyncBefore))
                ))
            .OrderBy(v => v.DetailSyncedAtUtc)
            .Select(v => v.Id)
            .Take(VideoBatchSize * VideoBatchesPerRun)
            .ToListAsync(ct);

        logger.LogInformation(
            "PrdbVideoDetailSyncService: syncing details for {Count} videos this run ({Pending} total pending), batches of {BatchSize}",
            videoIds.Count, totalPending, VideoBatchSize);

        var existingActorIds = await db.PrdbActors
            .Select(a => a.Id)
            .ToHashSetAsync(ct);

        var synced = 0;

        foreach (var batch in videoIds.Chunk(VideoBatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var request  = new PrdbApiBatchVideosRequest(batch.ToList());
            var response = await http.PostAsJsonAsync("videos/batch", request, ct);
            response.EnsureSuccessStatusCode();

            var details = await response.Content.ReadFromJsonAsync<List<PrdbApiVideoDetail>>(JsonOptions, ct);
            if (details is null) continue;

            var now = DateTime.UtcNow;
            var ids = batch.ToHashSet();

            // Load existing image IDs for the whole batch upfront to avoid identity map
            // collisions when the same image ID appears for multiple videos in one batch.
            var existingImageIds = await db.PrdbVideoImages
                .Where(i => ids.Contains(i.VideoId))
                .Select(i => i.Id)
                .ToHashSetAsync(ct);

            // Load existing VideoActor joins for the batch upfront
            var existingVideoActorPairs = await db.PrdbVideoActors
                .Where(va => ids.Contains(va.VideoId))
                .Select(va => new { va.VideoId, va.ActorId })
                .ToListAsync(ct);
            var existingVideoActorSet = existingVideoActorPairs
                .Select(va => (va.VideoId, va.ActorId))
                .ToHashSet();

            foreach (var detail in details)
            {
                // Upsert images
                foreach (var img in detail.Images.Where(i => existingImageIds.Add(i.Id)))
                {
                    db.PrdbVideoImages.Add(new PrdbVideoImage
                    {
                        Id      = img.Id,
                        CdnPath = img.CdnPath,
                        VideoId = detail.Id,
                    });
                }

                // Upsert VideoActor join entries; insert actor stubs for unknown actors
                foreach (var actor in detail.Actors)
                {
                    if (existingVideoActorSet.Add((detail.Id, actor.Id)))
                    {
                        db.PrdbVideoActors.Add(new PrdbVideoActor
                        {
                            VideoId = detail.Id,
                            ActorId = actor.Id,
                        });
                    }

                    if (existingActorIds.Add(actor.Id))
                    {
                        db.PrdbActors.Add(new PrdbActor
                        {
                            Id               = actor.Id,
                            Name             = actor.Name,
                            Gender           = actor.Gender,
                            Birthday         = actor.Birthday,
                            Nationality      = actor.Nationality,
                            PrdbCreatedAtUtc = now,
                            PrdbUpdatedAtUtc = now,
                            SyncedAtUtc      = now,
                        });
                    }
                }

                // Mark video detail as synced and backfill the authoritative prdb.net creation time
                var video = await db.PrdbVideos.FindAsync([detail.Id], ct);
                if (video is not null)
                {
                    video.DetailSyncedAtUtc = now;
                    video.PrdbCreatedAtUtc  = detail.CreatedAtUtc;
                }
            }

            // Mark videos silently omitted by the API as synced so they aren't retried
            foreach (var missingId in ids.Except(details.Select(d => d.Id)))
            {
                var video = await db.PrdbVideos.FindAsync([missingId], ct);
                if (video is not null)
                    video.DetailSyncedAtUtc = now;
            }

            await db.SaveChangesAsync(ct);
            synced += details.Count;
        }

        logger.LogInformation("PrdbVideoDetailSyncService: synced details for {Count} videos", synced);
    }

    // ── Phase 2: Actor detail batch sync ─────────────────────────────────────

    private async Task SyncActorDetailsAsync(HttpClient http, CancellationToken ct)
    {
        var limit = ActorBatchSize * ActorBatchesPerRun;

        var actorIds = await db.PrdbActors
            .Where(a => a.DetailSyncedAtUtc == null)
            .OrderBy(a => a.SyncedAtUtc)
            .Select(a => a.Id)
            .Take(limit)
            .ToListAsync(ct);

        if (actorIds.Count == 0)
        {
            logger.LogInformation("PrdbVideoDetailSyncService: no actors pending detail sync");
            return;
        }

        var totalPending = await db.PrdbActors.CountAsync(a => a.DetailSyncedAtUtc == null, ct);
        logger.LogInformation(
            "PrdbVideoDetailSyncService: syncing details for {Count} actors this run ({Pending} total pending), batches of {BatchSize}",
            actorIds.Count, totalPending, ActorBatchSize);

        var synced = 0;

        foreach (var batch in actorIds.Chunk(ActorBatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var request  = new PrdbApiBatchActorsRequest(batch.ToList());
            var response = await http.PostAsJsonAsync("actors/batch", request, ct);
            response.EnsureSuccessStatusCode();

            var details = await response.Content.ReadFromJsonAsync<List<PrdbApiActorDetail>>(JsonOptions, ct);
            if (details is null) continue;

            var now    = DateTime.UtcNow;
            var ids    = batch.ToHashSet();
            var actors = await db.PrdbActors
                .Include(a => a.Aliases)
                .Where(a => ids.Contains(a.Id))
                .ToListAsync(ct);

            var actorMap = actors.ToDictionary(a => a.Id);

            // Load existing image IDs for the whole batch upfront to avoid the identity
            // map issue where two actors sharing the same image ID in one batch would
            // cause EF Core to flip the entity from Added → Modified, producing an UPDATE
            // against a non-existent row.
            var seenImageIds = await db.PrdbActorImages
                .Where(i => ids.Contains(i.ActorId))
                .Select(i => i.Id)
                .ToHashSetAsync(ct);

            foreach (var detail in details)
            {
                if (!actorMap.TryGetValue(detail.Id, out var actor)) continue;

                actor.Name             = detail.Name;
                actor.Gender           = detail.Gender;
                actor.Birthday         = detail.Birthday;
                actor.BirthdayType     = detail.BirthdayType;
                actor.Deathday         = detail.Deathday;
                actor.Birthplace       = detail.Birthplace;
                actor.Haircolor        = detail.Haircolor;
                actor.Eyecolor         = detail.Eyecolor;
                actor.BreastType       = detail.BreastType;
                actor.Height           = detail.Height;
                actor.BraSize          = detail.BraSize;
                actor.BraSizeLabel     = detail.BraSizeLabel;
                actor.WaistSize        = detail.WaistSize;
                actor.HipSize          = detail.HipSize;
                actor.Nationality      = detail.Nationality;
                actor.Ethnicity        = detail.Ethnicity;
                actor.CareerStart      = detail.CareerStart;
                actor.CareerEnd        = detail.CareerEnd;
                actor.Tattoos          = detail.Tattoos;
                actor.Piercings        = detail.Piercings;
                actor.PrdbUpdatedAtUtc = detail.UpdatedAtUtc;
                actor.SyncedAtUtc      = now;
                actor.DetailSyncedAtUtc = now;

                // Upsert aliases
                var existingAliasNames = actor.Aliases.Select(a => a.Name).ToHashSet();
                foreach (var alias in detail.Aliases.Where(a => !existingAliasNames.Contains(a.Name)))
                {
                    actor.Aliases.Add(new PrdbActorAlias { Name = alias.Name, SiteId = alias.SiteId });
                }

                // Upsert images — use db.Add directly and a batch-level seen set to avoid
                // identity map collisions when the same image ID appears for multiple actors.
                foreach (var img in detail.Images.Where(i => seenImageIds.Add(i.Id)))
                {
                    db.PrdbActorImages.Add(new PrdbActorImage
                    {
                        Id        = img.Id,
                        ImageType = img.ImageType,
                        Url       = img.Url,
                        ActorId   = actor.Id,
                    });
                }

                synced++;
            }

            // Mark any actor in the batch that the API silently omitted (doesn't exist)
            foreach (var missingId in ids.Except(details.Select(d => d.Id)))
            {
                if (actorMap.TryGetValue(missingId, out var actor))
                    actor.DetailSyncedAtUtc = now;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
            {
                foreach (var entry in ex.Entries)
                {
                    var id = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue;
                    logger.LogWarning(
                        "PrdbVideoDetailSyncService: concurrency conflict on {EntityType} {Id} (expected 1 row, got 0) — clearing change tracker, will retry next run",
                        entry.Entity.GetType().Name, id);
                }
                db.ChangeTracker.Clear();
            }
        }

        logger.LogInformation("PrdbVideoDetailSyncService: synced details for {Count} actors", synced);
    }
}
