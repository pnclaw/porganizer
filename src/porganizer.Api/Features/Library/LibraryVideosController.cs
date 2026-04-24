using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Library;

[ApiController]
[Route("api/library-videos")]
[Produces("application/json")]
public class LibraryVideosController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List library videos")]
    [EndpointDescription("Returns a paged list of PrdbVideos that have at least one matched local file, ordered by release date descending.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] Guid? siteId,
        [FromQuery] Guid? folderId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24)
    {
        var q = db.PrdbVideos
            .Where(v => db.LibraryFiles.Any(f => f.VideoId == v.Id));

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(v => EF.Functions.Like(v.Title, $"%{search}%"));

        if (siteId.HasValue)
            q = q.Where(v => v.SiteId == siteId.Value);

        if (folderId.HasValue)
            q = q.Where(v => db.LibraryFiles.Any(f => f.VideoId == v.Id && f.LibraryFolderId == folderId.Value));

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(v => v.ReleaseDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new LibraryVideoListItem
            {
                Id = v.Id,
                Title = v.Title,
                ReleaseDate = v.ReleaseDate,
                SiteId = v.SiteId,
                SiteTitle = v.Site.Title,
                ThumbnailCdnPath = db.PrdbVideoUserImages
                    .Where(i => i.VideoId == v.Id
                             && i.PreviewImageType == "Single"
                             && i.ModerationVisibility == "Public")
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => i.Url)
                    .FirstOrDefault()
                    ?? v.Images.Select(i => i.CdnPath).FirstOrDefault(),
                ActorCount = v.VideoActors.Count,
                LocalFileCount = db.LibraryFiles.Count(f => f.VideoId == v.Id),
                SpriteSheetCdnUrl = db.PrdbVideoUserImages
                    .Where(i => i.VideoId == v.Id && i.PreviewImageType == "SpriteSheet" && i.ModerationVisibility == "Public")
                    .Select(i => i.Url)
                    .FirstOrDefault(),
                SpriteTileCount = db.PrdbVideoUserImages
                    .Where(i => i.VideoId == v.Id && i.PreviewImageType == "SpriteSheet" && i.ModerationVisibility == "Public")
                    .Select(i => i.SpriteTileCount)
                    .FirstOrDefault(),
                SpriteColumns = db.PrdbVideoUserImages
                    .Where(i => i.VideoId == v.Id && i.PreviewImageType == "SpriteSheet" && i.ModerationVisibility == "Public")
                    .Select(i => i.SpriteColumns)
                    .FirstOrDefault(),
                SpriteRows = db.PrdbVideoUserImages
                    .Where(i => i.VideoId == v.Id && i.PreviewImageType == "SpriteSheet" && i.ModerationVisibility == "Public")
                    .Select(i => i.SpriteRows)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(new { items, total });
    }

    [HttpGet("filter-options")]
    [EndpointSummary("Get filter options for library videos")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFilterOptions()
    {
        var sites = await db.PrdbSites
            .Where(s => db.PrdbVideos.Any(v =>
                v.SiteId == s.Id &&
                db.LibraryFiles.Any(f => f.VideoId == v.Id)))
            .OrderBy(s => s.Title)
            .Select(s => new { s.Id, s.Title })
            .ToListAsync();

        var folders = await db.LibraryFolders
            .Where(f => f.Files.Any(lf => lf.VideoId != null))
            .OrderBy(f => f.Path)
            .Select(f => new { f.Id, Label = f.Label ?? f.Path, f.Path })
            .ToListAsync();

        return Ok(new { sites, folders });
    }

    [HttpGet("{id:guid}")]
    [EndpointSummary("Get library video detail")]
    [EndpointDescription("Returns full video detail plus matched local files.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id)
    {
        var video = await db.PrdbVideos
            .Include(v => v.Site)
            .Include(v => v.Images)
            .Include(v => v.PreDbEntries)
            .Include(v => v.VideoActors)
                .ThenInclude(va => va.Actor)
            .AsSplitQuery()
            .FirstOrDefaultAsync(v => v.Id == id);

        if (video is null) return NotFound();

        var userImageUrls = await db.PrdbVideoUserImages
            .Where(i => i.VideoId == id && i.ModerationVisibility == "Public" && i.PreviewImageType != "SpriteSheet")
            .OrderBy(i => i.DisplayOrder)
            .Select(i => i.Url)
            .ToListAsync();

        var prdbImagePaths = video.Images
            .Where(i => i.CdnPath != null)
            .Select(i => i.CdnPath!)
            .ToList();

        var spriteImage = await db.PrdbVideoUserImages
            .Where(i => i.VideoId == id && i.PreviewImageType == "SpriteSheet" && i.ModerationVisibility == "Public")
            .Select(i => new { i.Url, i.SpriteTileCount, i.SpriteColumns, i.SpriteRows })
            .FirstOrDefaultAsync();

        var localFiles = await db.LibraryFiles
            .Where(f => f.VideoId == id)
            .Select(f => new LibraryFileResponse
            {
                Id = f.Id,
                FolderId = f.LibraryFolderId,
                FolderPath = f.Folder.Path,
                RelativePath = f.RelativePath,
                FileSize = f.FileSize,
                OsHash = f.OsHash,
                LastSeenAtUtc = f.LastSeenAtUtc,
            })
            .OrderBy(f => f.RelativePath)
            .ToListAsync();

        return Ok(new LibraryVideoDetail
        {
            Id = video.Id,
            Title = video.Title,
            ReleaseDate = video.ReleaseDate,
            SiteId = video.SiteId,
            SiteTitle = video.Site.Title,
            SiteUrl = video.Site.Url,
            UserImageCdnPaths = userImageUrls.Count > 0 ? userImageUrls : [],
            PrdbImagePaths = prdbImagePaths,
            SpriteSheetCdnUrl = spriteImage?.Url,
            SpriteTileCount = spriteImage?.SpriteTileCount,
            SpriteColumns = spriteImage?.SpriteColumns,
            SpriteRows = spriteImage?.SpriteRows,
            Actors = video.VideoActors
                .Select(va => new LibraryVideoActorResponse
                {
                    Id = va.Actor.Id,
                    Name = va.Actor.Name,
                })
                .OrderBy(a => a.Name)
                .ToList(),
            PreNames = video.PreDbEntries.Select(p => p.Title).ToList(),
            LocalFiles = localFiles,
        });
    }
}

public class LibraryVideoListItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly? ReleaseDate { get; set; }
    public Guid SiteId { get; set; }
    public string SiteTitle { get; set; } = string.Empty;
    public string? ThumbnailCdnPath { get; set; }
    public int ActorCount { get; set; }
    public int LocalFileCount { get; set; }
    public string? SpriteSheetCdnUrl { get; set; }
    public int? SpriteTileCount { get; set; }
    public int? SpriteColumns { get; set; }
    public int? SpriteRows { get; set; }
}

public class LibraryVideoDetail
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly? ReleaseDate { get; set; }
    public Guid SiteId { get; set; }
    public string SiteTitle { get; set; } = string.Empty;
    public string? SiteUrl { get; set; }
    public List<string> UserImageCdnPaths { get; set; } = [];
    public List<string> PrdbImagePaths { get; set; } = [];
    public string? SpriteSheetCdnUrl { get; set; }
    public int? SpriteTileCount { get; set; }
    public int? SpriteColumns { get; set; }
    public int? SpriteRows { get; set; }
    public List<LibraryVideoActorResponse> Actors { get; set; } = [];
    public List<string> PreNames { get; set; } = [];
    public List<LibraryFileResponse> LocalFiles { get; set; } = [];
}

public class LibraryVideoActorResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class LibraryFileResponse
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? OsHash { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}
