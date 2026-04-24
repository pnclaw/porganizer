using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Api.Common;
using porganizer.Database;

namespace porganizer.Api.Features.Indexers.Search;

[ApiController]
[Route("api/usenet-search")]
[Produces("application/json")]
public class UsenetSearchController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("Search usenet indexer rows")]
    [EndpointDescription("Returns a paginated list of indexer rows enriched with video match hints and preview images.")]
    [ProducesResponseType(typeof(UsenetSearchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] UsenetSearchQuery query, CancellationToken ct)
    {
        var indexers = await db.Indexers.ToListAsync(ct);

        var indexerSourceMap = indexers
            .Where(i => IndexerSourceMapper.TryMap(i.Url, out _))
            .ToDictionary(
                i => i.Id,
                i => { IndexerSourceMapper.TryMap(i.Url, out var s); return s; });

        var q = db.IndexerRows.AsQueryable();

        if (query.IndexerIds is { Length: > 0 })
            q = q.Where(r => query.IndexerIds.Contains(r.IndexerId));

        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(r => EF.Functions.Like(r.Title, $"%{query.Search}%"));

        if (query.PreviewMode)
        {
            var drunkenSlugIndexerIds = indexerSourceMap
                .Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();
            var nzbFinderIndexerIds = indexerSourceMap
                .Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();

            q = q.Where(r =>
                db.IndexerRowMatches.Any(m => m.IndexerRowId == r.Id) ||
                (drunkenSlugIndexerIds.Contains(r.IndexerId) &&
                    db.PrdbIndexerFilehashes.Any(f => f.IndexerSource == 0 && f.IndexerId == r.NzbId && !f.IsDeleted)) ||
                (nzbFinderIndexerIds.Contains(r.IndexerId) &&
                    db.PrdbIndexerFilehashes.Any(f => f.IndexerSource == 1 && f.IndexerId == r.NzbId && !f.IsDeleted)));
        }

        var total = await q.CountAsync(ct);

        var rows = await q
            .OrderByDescending(r => r.NzbPublishedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new { r.Id, r.IndexerId, r.NzbId, r.Title, r.NzbUrl, r.NzbSize, r.NzbPublishedAt })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return Ok(new UsenetSearchResponse([], total));

        var rowIds = rows.Select(r => r.Id).ToList();

        var matchByRowId = await db.IndexerRowMatches
            .Where(m => rowIds.Contains(m.IndexerRowId))
            .ToDictionaryAsync(m => m.IndexerRowId, ct);

        var videoIds = matchByRowId.Values.Select(m => m.PrdbVideoId).Distinct().ToList();

        // Prefer user-uploaded Single/Public preview images, fall back to standard CDN images
        var userImageByVideoId = (await db.PrdbVideoUserImages
            .Where(img => videoIds.Contains(img.VideoId)
                       && img.PreviewImageType == "Single"
                       && img.ModerationVisibility == "Public")
            .OrderBy(img => img.VideoId)
            .ThenBy(img => img.DisplayOrder)
            .ToListAsync(ct))
            .GroupBy(img => img.VideoId)
            .ToDictionary(g => g.Key, g => g.First().Url);

        var cdnImageByVideoId = (await db.PrdbVideoImages
            .Where(img => videoIds.Contains(img.VideoId) && img.CdnPath != null)
            .ToListAsync(ct))
            .GroupBy(img => img.VideoId)
            .ToDictionary(g => g.Key, g => g.First().CdnPath!);

        var videoTitleById = await db.PrdbVideos
            .Where(v => videoIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Title })
            .ToDictionaryAsync(v => v.Id, v => v.Title, ct);

        var rowsWithFilehash = new HashSet<Guid>();
        var nzbIdsBySource = rows
            .Where(r => indexerSourceMap.ContainsKey(r.IndexerId))
            .GroupBy(r => indexerSourceMap[r.IndexerId])
            .ToDictionary(g => g.Key, g => g.Select(r => r.NzbId).ToList());

        foreach (var (sourceInt, nzbIds) in nzbIdsBySource)
        {
            var s = sourceInt;
            var matchedNzbIds = await db.PrdbIndexerFilehashes
                .Where(f => f.IndexerSource == s && nzbIds.Contains(f.IndexerId) && !f.IsDeleted)
                .Select(f => f.IndexerId)
                .ToListAsync(ct);
            var matchedSet = matchedNzbIds.ToHashSet();
            foreach (var r in rows.Where(r => matchedSet.Contains(r.NzbId)))
                rowsWithFilehash.Add(r.Id);
        }

        var indexerNameById = indexers.ToDictionary(i => i.Id, i => i.Title);

        var items = rows.Select(r =>
        {
            matchByRowId.TryGetValue(r.Id, out var match);
            var videoId = match?.PrdbVideoId;
            userImageByVideoId.TryGetValue(videoId ?? Guid.Empty, out var imageUrl);
            if (imageUrl is null && videoId.HasValue)
                cdnImageByVideoId.TryGetValue(videoId.Value, out imageUrl);
            videoTitleById.TryGetValue(videoId ?? Guid.Empty, out var videoTitle);
            indexerNameById.TryGetValue(r.IndexerId, out var indexerName);

            return new UsenetSearchRowResponse(
                r.Id,
                r.IndexerId,
                indexerName ?? string.Empty,
                r.Title,
                r.NzbUrl,
                r.NzbSize,
                r.NzbPublishedAt,
                videoId,
                videoTitle,
                imageUrl,
                rowsWithFilehash.Contains(r.Id));
        }).ToList();

        return Ok(new UsenetSearchResponse(items, total));
    }
}
