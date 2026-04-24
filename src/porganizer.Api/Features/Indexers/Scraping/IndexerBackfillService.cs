using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.Indexers.Scraping;

public class IndexerBackfillService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<IndexerBackfillService> logger)
{
    private const int PageSize = 100;
    private const int PagesPerRunPerIndexer = 3;
    private const int Category = 6000;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var enabledIndexers = await db.Indexers
            .Where(i => i.IsEnabled)
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Title)
            .ToListAsync(ct);

        foreach (var indexer in enabledIndexers)
            await RunIndexerAsync(indexer, now, ct);
    }

    private async Task<IndexerPageFetchResult> FetchPageAsync(Indexer indexer, int offset, CancellationToken ct)
    {
        var baseUrl = $"{indexer.Url.TrimEnd('/')}{indexer.ApiPath}";
        var url = $"{baseUrl}?t=search&cat={Category}&apikey={indexer.ApiKey}&offset={offset}&limit={PageSize}";
        var client = httpClientFactory.CreateClient();
        var sw = Stopwatch.StartNew();

        try
        {
            var response = await client.GetAsync(url, ct);
            sw.Stop();
            var xml = await response.Content.ReadAsStringAsync(ct);

            return new IndexerPageFetchResult
            {
                Success = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                Items = response.IsSuccessStatusCode ? NewznabParser.Parse(xml) : [],
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            logger.LogWarning(ex, "IndexerBackfillService: failed to fetch offset {Offset} for indexer {Title}", offset, indexer.Title);
            return new IndexerPageFetchResult
            {
                Success = false,
                StatusCode = null,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                Items = [],
            };
        }
    }

    public async Task RunIndexerAsync(Guid indexerId, CancellationToken ct = default)
    {
        var indexer = await db.Indexers.FirstOrDefaultAsync(i => i.Id == indexerId, ct);
        if (indexer is null || !indexer.IsEnabled)
            return;

        await RunIndexerAsync(indexer, DateTime.UtcNow, ct);
    }

    private async Task RunIndexerAsync(Indexer indexer, DateTime now, CancellationToken ct)
    {
        if (indexer.BackfillCompletedAtUtc is not null)
        {
            logger.LogDebug("IndexerBackfillService: backfill already completed for indexer {Title}", indexer.Title);
            return;
        }

        indexer.BackfillLastRunAtUtc = now;
        indexer.BackfillStartedAtUtc ??= now;
        indexer.BackfillCutoffUtc ??= now.AddDays(-indexer.BackfillDays);
        indexer.BackfillCurrentOffset ??= 0;

        var cutoffUtc = indexer.BackfillCutoffUtc.Value;

        for (var page = 0; page < PagesPerRunPerIndexer; page++)
        {
            var offset = indexer.BackfillCurrentOffset ?? 0;
            var result = await FetchPageAsync(indexer, offset, ct);
            db.IndexerApiRequests.Add(MakeSearchRequest(indexer.Id, result.Success, result.StatusCode, result.ResponseTimeMs));

            if (!result.Success)
            {
                await db.SaveChangesAsync(ct);
                logger.LogWarning("IndexerBackfillService: stopping run after failed request for indexer {Title} at offset {Offset}", indexer.Title, offset);
                return;
            }

            if (result.Items.Count == 0)
            {
                MarkIndexerCompleted(indexer, now);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("IndexerBackfillService: indexer {Title} returned no more results at offset {Offset}", indexer.Title, offset);
                return;
            }

            var existingNzbIds = await db.IndexerRows
                .Where(r => r.IndexerId == indexer.Id)
                .Select(r => r.NzbId)
                .ToHashSetAsync(ct);

            var newRows = new List<IndexerRow>();
            var pageReachedCutoff = true;

            foreach (var item in result.Items)
            {
                var isWithinWindow = item.NzbPublishedAt is null || item.NzbPublishedAt >= cutoffUtc;
                if (isWithinWindow)
                    pageReachedCutoff = false;

                if (!isWithinWindow || string.IsNullOrEmpty(item.NzbId) || !existingNzbIds.Add(item.NzbId))
                    continue;

                newRows.Add(new IndexerRow
                {
                    Id = Guid.NewGuid(),
                    IndexerId = indexer.Id,
                    Title = item.Title,
                    NzbId = item.NzbId,
                    NzbUrl = item.NzbUrl,
                    NzbSize = item.NzbSize,
                    NzbPublishedAt = item.NzbPublishedAt,
                    FileSize = item.FileSize,
                    Category = item.Category,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            if (newRows.Count > 0)
                db.IndexerRows.AddRange(newRows);

            if (pageReachedCutoff)
            {
                logger.LogInformation(
                    "IndexerBackfillService: indexer {Title} reached cutoff {Cutoff} at offset {Offset}",
                    indexer.Title, cutoffUtc, offset);
                MarkIndexerCompleted(indexer, now);
            }
            else
            {
                indexer.BackfillCurrentOffset = offset + PageSize;
            }

            await db.SaveChangesAsync(ct);

            if (newRows.Count > 0)
            {
                logger.LogInformation(
                    "IndexerBackfillService: saved {Count} new rows for indexer {Title} at offset {Offset}",
                    newRows.Count, indexer.Title, offset);
            }

            if (indexer.BackfillCompletedAtUtc is not null)
                return;
        }
    }

    private static void MarkIndexerCompleted(Indexer indexer, DateTime now)
    {
        indexer.BackfillCompletedAtUtc = now;
        indexer.BackfillCurrentOffset = null;
    }

    private static IndexerApiRequest MakeSearchRequest(Guid indexerId, bool success, int? statusCode, int responseTimeMs) => new()
    {
        Id = Guid.NewGuid(),
        IndexerId = indexerId,
        RequestType = IndexerRequestType.Search,
        OccurredAt = DateTime.UtcNow,
        Success = success,
        HttpStatusCode = statusCode,
        ResponseTimeMs = responseTimeMs,
    };

    private sealed class IndexerPageFetchResult
    {
        public bool Success { get; init; }
        public int? StatusCode { get; init; }
        public int ResponseTimeMs { get; init; }
        public IReadOnlyList<ParsedIndexerRow> Items { get; init; } = [];
    }
}
