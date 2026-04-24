using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Prdb.Sync;

public class PrdbActorSyncService(AppDbContext db, IHttpClientFactory httpClientFactory, ILogger<PrdbActorSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const int PageSize    = 500;
    private const int PagesPerRun = 10; // 5 000 actors per run, 10 API requests

    public async Task RunAsync(CancellationToken ct)
    {
        var settings = await db.GetSettingsAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.PrdbApiKey))
        {
            logger.LogWarning("PrdbActorSyncService: PrdbApiKey not configured, skipping");
            return;
        }

        var http = CreateClient(settings);

        if (settings.PrdbActorSyncPage is not null)
            await RunBackfillPageAsync(http, settings, ct);
        else if (settings.PrdbActorLastSyncedAt is not null)
            await RunNewActorCheckAsync(http, settings, ct);
    }

    // ── Backfill (up to PagesPerRun pages per run) ───────────────────────────

    private async Task RunBackfillPageAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        var startPage     = settings.PrdbActorSyncPage!.Value;
        var currentPage   = startPage;
        var totalInserted = 0;
        var done          = false;

        logger.LogInformation("PrdbActorSyncService: backfill starting at page {Page}", startPage);

        for (var i = 0; i < PagesPerRun; i++)
        {
            var url      = $"actors?Page={currentPage}&PageSize={PageSize}&SortBy=createdAtUtc&SortDirection=asc";
            var response = await http.GetFromJsonAsync<PrdbApiPagedResult<PrdbApiActorSummary>>(url, JsonOptions, ct);

            if (response is null || response.Items.Count == 0)
            {
                done = true;
                break;
            }

            totalInserted                += await UpsertNewActorsAsync(response.Items, ct);
            settings.PrdbActorTotalCount  = response.TotalCount;

            var fetched = (long)currentPage * PageSize;
            currentPage++;

            if (fetched >= response.TotalCount)
            {
                done = true;
                break;
            }
        }

        settings.PrdbActorSyncPage     = done ? null : currentPage;
        settings.PrdbActorLastSyncedAt = done ? DateTime.UtcNow : settings.PrdbActorLastSyncedAt;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PrdbActorSyncService: backfill pages {Start}–{End} — inserted {Inserted}, total {Total}, next: {Next}",
            startPage, currentPage - 1, totalInserted, settings.PrdbActorTotalCount,
            settings.PrdbActorSyncPage?.ToString() ?? "done");
    }

    // ── New-actor check (runs every tick after backfill) ─────────────────────

    private async Task RunNewActorCheckAsync(HttpClient http, AppSettings settings, CancellationToken ct)
    {
        var since = settings.PrdbActorLastSyncedAt!.Value;
        logger.LogInformation("PrdbActorSyncService: checking for new actors since {Since:O}", since);

        var sinceEncoded = Uri.EscapeDataString(since.ToString("O"));
        var allActors    = new List<PrdbApiActorSummary>();
        var page         = 1;

        while (true)
        {
            var url      = $"actors?CreatedAfter={sinceEncoded}&SortBy=createdAtUtc&SortDirection=asc&Page={page}&PageSize={PageSize}";
            var response = await http.GetFromJsonAsync<PrdbApiPagedResult<PrdbApiActorSummary>>(url, JsonOptions, ct);

            if (response is null || response.Items.Count == 0) break;

            allActors.AddRange(response.Items);

            if (allActors.Count >= response.TotalCount) break;

            page++;
        }

        var inserted = allActors.Count > 0 ? await UpsertNewActorsAsync(allActors, ct) : 0;

        settings.PrdbActorLastSyncedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PrdbActorSyncService: new-actor check complete — {Found} found, {Inserted} inserted",
            allActors.Count, inserted);
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    private async Task<int> UpsertNewActorsAsync(List<PrdbApiActorSummary> actors, CancellationToken ct)
    {
        var incomingIds = actors.Select(a => a.Id).ToList();
        var existingIds = await db.PrdbActors
            .Where(a => incomingIds.Contains(a.Id))
            .Select(a => a.Id)
            .ToHashSetAsync(ct);

        var now      = DateTime.UtcNow;
        var toInsert = actors
            .Where(a => !existingIds.Contains(a.Id))
            .Select(a => new PrdbActor
            {
                Id               = a.Id,
                Name             = a.Name,
                Gender           = a.Gender,
                Birthday         = a.Birthday,
                Nationality      = a.Nationality,
                Ethnicity        = a.Ethnicity,
                PrdbCreatedAtUtc = now,
                PrdbUpdatedAtUtc = now,
                SyncedAtUtc      = now,
                Images           = a.ProfileImageUrl is not null
                    ? [ new PrdbActorImage { Id = Guid.NewGuid(), ImageType = 0, Url = a.ProfileImageUrl } ]
                    : [],
            })
            .ToList();

        if (toInsert.Count > 0)
        {
            db.PrdbActors.AddRange(toInsert);
            await db.SaveChangesAsync(ct);
        }

        return toInsert.Count;
    }

    private HttpClient CreateClient(AppSettings settings)
    {
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(settings.PrdbApiUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", settings.PrdbApiKey);
        return http;
    }
}
