using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.Indexers.Scraping;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.Indexers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class IndexersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List all indexers")]
    [EndpointDescription("Returns all indexers ordered by creation date descending.")]
    [ProducesResponseType(typeof(IEnumerable<IndexerResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var indexers = await db.Indexers
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Ok(indexers.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    [EndpointSummary("Get indexer by ID")]
    [EndpointDescription("Returns a single indexer by its unique identifier.")]
    [ProducesResponseType(typeof(IndexerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var indexer = await db.Indexers.FindAsync(id);
        return indexer is null ? NotFound() : Ok(ToResponse(indexer));
    }

    [HttpPost]
    [Consumes("application/json")]
    [EndpointSummary("Create indexer")]
    [EndpointDescription("Creates a new indexer and returns the created resource.")]
    [ProducesResponseType(typeof(IndexerResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateIndexerRequest request)
    {
        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Url = request.Url,
            ParsingType = request.ParsingType,
            IsEnabled = request.IsEnabled,
            ApiKey = request.ApiKey,
            ApiPath = request.ApiPath,
            BackfillDays = request.BackfillDays,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.Indexers.Add(indexer);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = indexer.Id }, ToResponse(indexer));
    }

    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [EndpointSummary("Update indexer")]
    [EndpointDescription("Updates an existing indexer by its unique identifier.")]
    [ProducesResponseType(typeof(IndexerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIndexerRequest request)
    {
        var indexer = await db.Indexers.FindAsync(id);
        if (indexer is null) return NotFound();

        indexer.Title = request.Title;
        indexer.Url = request.Url;
        indexer.ParsingType = request.ParsingType;
        indexer.IsEnabled = request.IsEnabled;
        indexer.ApiKey = request.ApiKey;
        indexer.ApiPath = request.ApiPath;
        if (request.BackfillDays > indexer.BackfillDays)
            ResetBackfillState(indexer);
        indexer.BackfillDays = request.BackfillDays;
        indexer.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(ToResponse(indexer));
    }

    [HttpPost("test")]
    [Consumes("application/json")]
    [EndpointSummary("Test indexer connection")]
    [EndpointDescription("Tests connectivity to an indexer using its caps endpoint.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Test([FromBody] TestIndexerRequest request, [FromServices] IndexerScrapeService scraper)
    {
        var (success, message) = await scraper.TestIndexerAsync(request.Url, request.ApiPath, request.ApiKey);
        return Ok(new { success, message });
    }

    [HttpGet("{id:guid}/stats")]
    [EndpointSummary("Get indexer stats")]
    [EndpointDescription("Returns aggregated statistics for an indexer over the last 30 days.")]
    [ProducesResponseType(typeof(IndexerStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStats(Guid id)
    {
        if (!await db.Indexers.AnyAsync(i => i.Id == id)) return NotFound();

        var cutoff = DateTime.UtcNow.Date.AddDays(-29);

        // All-time summary — computed in DB
        var totalSearch = await db.IndexerApiRequests.LongCountAsync(r => r.IndexerId == id && r.RequestType == IndexerRequestType.Search);
        var totalGrabs  = await db.IndexerApiRequests.LongCountAsync(r => r.IndexerId == id && r.RequestType == IndexerRequestType.Grab);
        var totalRows   = await db.IndexerRows.LongCountAsync(r => r.IndexerId == id);
        var avgRt       = await db.IndexerApiRequests
            .Where(r => r.IndexerId == id && r.RequestType == IndexerRequestType.Search && r.ResponseTimeMs != null)
            .AverageAsync(r => (double?)r.ResponseTimeMs);

        // Last 30 days — pull into memory for reliable date grouping with SQLite
        var recent = await db.IndexerApiRequests
            .Where(r => r.IndexerId == id && r.OccurredAt >= cutoff)
            .Select(r => new { r.OccurredAt, r.RequestType, r.Success, r.ResponseTimeMs })
            .ToListAsync();

        var searchSuccess = recent.Count(r => r.RequestType == IndexerRequestType.Search && r.Success);
        var searchFailure = recent.Count(r => r.RequestType == IndexerRequestType.Search && !r.Success);

        // Group by calendar date
        var byDate = recent.GroupBy(r => r.OccurredAt.Date).ToDictionary(g => g.Key, g => g.ToList());

        var dates = Enumerable.Range(0, 30).Select(i => cutoff.AddDays(i)).ToList();

        var requestsPerDay = dates.Select(date =>
        {
            if (!byDate.TryGetValue(date, out var items))
                return new DailyRequestStat(date.ToString("yyyy-MM-dd"), 0, 0);
            var search = items.Count(r => r.RequestType == IndexerRequestType.Search);
            var grab   = items.Count(r => r.RequestType == IndexerRequestType.Grab);
            return new DailyRequestStat(date.ToString("yyyy-MM-dd"), search, grab);
        }).ToList();

        var avgRtPerDay = dates.Select(date =>
        {
            if (!byDate.TryGetValue(date, out var items)) return new DailyResponseTimeStat(date.ToString("yyyy-MM-dd"), 0);
            var avg = items
                .Where(r => r.RequestType == IndexerRequestType.Search && r.ResponseTimeMs.HasValue)
                .Select(r => (double)r.ResponseTimeMs!.Value)
                .DefaultIfEmpty(0)
                .Average();
            return new DailyResponseTimeStat(date.ToString("yyyy-MM-dd"), Math.Round(avg, 1));
        }).ToList();

        var rowsByCategory = await db.IndexerRows
            .Where(r => r.IndexerId == id)
            .GroupBy(r => r.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(10)
            .ToListAsync();

        return Ok(new IndexerStatsResponse
        {
            TotalSearchRequests = totalSearch,
            TotalGrabs          = totalGrabs,
            TotalRows           = totalRows,
            AvgResponseTimeMs   = avgRt.HasValue ? Math.Round(avgRt.Value, 1) : null,
            SearchSuccess       = searchSuccess,
            SearchFailure       = searchFailure,
            RequestsPerDay      = requestsPerDay,
            AvgResponseTimePerDay = avgRtPerDay,
            RowsByCategory      = rowsByCategory.Select(r => new CategoryStat(r.Category, r.Count)).ToList(),
        });
    }

    [HttpGet("{id:guid}/rows")]
    [EndpointSummary("List indexer rows")]
    [EndpointDescription("Returns a paginated, filtered list of rows scraped from this indexer.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRows(Guid id, [FromQuery] IndexerRowsQuery query)
    {
        if (!await db.Indexers.AnyAsync(i => i.Id == id)) return NotFound();

        var q = db.IndexerRows.Where(r => r.IndexerId == id);

        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(r => EF.Functions.Like(r.Title, $"%{query.Search}%"));

        if (query.Categories is { Length: > 0 })
            q = q.Where(r => query.Categories.Contains(r.Category));

        if (query.From.HasValue)
            q = q.Where(r => r.NzbPublishedAt >= query.From.Value);

        if (query.To.HasValue)
            q = q.Where(r => r.NzbPublishedAt <= query.To.Value);

        if (query.MinSize.HasValue)
            q = q.Where(r => r.NzbSize >= query.MinSize.Value);

        if (query.MaxSize.HasValue)
            q = q.Where(r => r.NzbSize <= query.MaxSize.Value);

        if (query.HasVideoLink.HasValue)
        {
            var linked = db.IndexerRowMatches.Select(m => m.IndexerRowId);
            q = query.HasVideoLink.Value
                ? q.Where(r => linked.Contains(r.Id))
                : q.Where(r => !linked.Contains(r.Id));
        }

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(r => r.NzbPublishedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new IndexerRowResponse
            {
                Id = r.Id,
                IndexerId = r.IndexerId,
                Title = r.Title,
                NzbId = r.NzbId,
                NzbUrl = r.NzbUrl,
                NzbSize = r.NzbSize,
                NzbPublishedAt = r.NzbPublishedAt,
                FileSize = r.FileSize,
                Category = r.Category,
                CreatedAt = r.CreatedAt,
                PrdbVideoId = db.IndexerRowMatches
                    .Where(m => m.IndexerRowId == r.Id)
                    .Select(m => (Guid?)m.PrdbVideoId)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(new { items, total });
    }

    [HttpGet("{id:guid}/rows/categories")]
    [EndpointSummary("List distinct categories for indexer")]
    [EndpointDescription("Returns the distinct category values present in the rows for this indexer.")]
    [ProducesResponseType(typeof(IEnumerable<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRowCategories(Guid id)
    {
        if (!await db.Indexers.AnyAsync(i => i.Id == id)) return NotFound();

        var categories = await db.IndexerRows
            .Where(r => r.IndexerId == id)
            .Select(r => r.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        return Ok(categories);
    }

    [HttpDelete("{id:guid}/rows")]
    [EndpointSummary("Clear indexer rows")]
    [EndpointDescription("Deletes all scraped rows for this indexer.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearRows(Guid id)
    {
        if (!await db.Indexers.AnyAsync(i => i.Id == id)) return NotFound();

        await db.IndexerRows.Where(r => r.IndexerId == id).ExecuteDeleteAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/backfill")]
    [EndpointSummary("Backfill indexer")]
    [EndpointDescription("Fetches and saves the specified number of pages from this indexer.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Backfill(Guid id, [FromQuery] int pages, [FromServices] IndexerScrapeService scraper)
    {
        if (pages < 1) return BadRequest(new { error = "pages must be at least 1" });

        var indexer = await db.Indexers.FindAsync(id);
        if (indexer is null) return NotFound();

        var newRows = await scraper.ScrapeIndexerAsync(indexer, pages);
        return Ok(new { newRows });
    }

    [HttpPost("{id:guid}/scrape")]
    [EndpointSummary("Scrape indexer")]
    [EndpointDescription("Fetches the latest NZBs from the indexer and saves new rows to the database.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Scrape(Guid id, [FromServices] IndexerScrapeService scraper)
    {
        var indexer = await db.Indexers.FindAsync(id);
        if (indexer is null) return NotFound();

        var newRows = await scraper.ScrapeIndexerAsync(indexer);
        return Ok(new { newRows });
    }

    [HttpDelete("{id:guid}")]
    [EndpointSummary("Delete indexer")]
    [EndpointDescription("Deletes an indexer by its unique identifier.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var indexer = await db.Indexers.FindAsync(id);
        if (indexer is null) return NotFound();

        db.Indexers.Remove(indexer);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static IndexerResponse ToResponse(Indexer indexer) => new()
    {
        Id = indexer.Id,
        Title = indexer.Title,
        Url = indexer.Url,
        ParsingType = (int)indexer.ParsingType,
        IsEnabled = indexer.IsEnabled,
        ApiKey = indexer.ApiKey,
        ApiPath = indexer.ApiPath,
        BackfillDays = indexer.BackfillDays,
        BackfillStartedAtUtc = indexer.BackfillStartedAtUtc,
        BackfillCutoffUtc = indexer.BackfillCutoffUtc,
        BackfillCompletedAtUtc = indexer.BackfillCompletedAtUtc,
        BackfillLastRunAtUtc = indexer.BackfillLastRunAtUtc,
        BackfillCurrentOffset = indexer.BackfillCurrentOffset,
        CreatedAt = indexer.CreatedAt,
        UpdatedAt = indexer.UpdatedAt,
    };

    private static void ResetBackfillState(Indexer indexer)
    {
        indexer.BackfillStartedAtUtc = null;
        indexer.BackfillCutoffUtc = null;
        indexer.BackfillCompletedAtUtc = null;
        indexer.BackfillLastRunAtUtc = null;
        indexer.BackfillCurrentOffset = null;
    }
}
