using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.Rescue;

public sealed class RescueTests : IAsyncLifetime
{
    private readonly PorganizerApiFactory _factory = new();
    private HttpClient _client = null!;
    private string _tempRoot = null!;

    public async Task InitializeAsync()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"porganizer-rescue-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);
        _client = _factory.CreateClient();
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task Preview_ReturnsBadRequest_WhenFolderDoesNotExist()
    {
        var response = await _client.PostAsJsonAsync("/api/rescue/preview", new { folder = "/nonexistent/path/xyz" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Preview_ReturnsMatchedItem_WhenSubfolderMatchesIndexerRow()
    {
        var subfolder = Path.Combine(_tempRoot, "Some.Scene.Title.1080p");
        Directory.CreateDirectory(subfolder);
        File.WriteAllBytes(Path.Combine(subfolder, "video.mkv"), new byte[1024]);

        var response = await _client.PostAsJsonAsync("/api/rescue/preview", new { folder = _tempRoot });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RescuePreviewDto>();
        body.Should().NotBeNull();

        var matched = body!.Items.Where(i => i.IsMatched).ToList();
        matched.Should().ContainSingle();
        matched[0].Name.Should().Be("Some.Scene.Title.1080p");
        matched[0].VideoTitle.Should().Be("Some Scene Title");
        matched[0].SiteTitle.Should().Be("Alpha Site");
        matched[0].VideoFileCount.Should().Be(1);
    }

    [Fact]
    public async Task Preview_ReturnsUnmatchedItem_WhenNoIndexerRowExists()
    {
        var subfolder = Path.Combine(_tempRoot, "Completely.Unknown.Title.720p");
        Directory.CreateDirectory(subfolder);
        File.WriteAllBytes(Path.Combine(subfolder, "video.mp4"), new byte[1024]);

        var response = await _client.PostAsJsonAsync("/api/rescue/preview", new { folder = _tempRoot });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RescuePreviewDto>();
        body.Should().NotBeNull();

        var unmatched = body!.Items.Where(i => !i.IsMatched && i.Name == "Completely.Unknown.Title.720p").ToList();
        unmatched.Should().ContainSingle();
    }

    [Fact]
    public async Task Execute_ReturnsBadRequest_WhenFolderDoesNotExist()
    {
        var response = await _client.PostAsJsonAsync("/api/rescue/execute", new { folder = "/nonexistent/path/xyz" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var targetFolder = Path.Combine(_tempRoot, "target");
        Directory.CreateDirectory(targetFolder);

        var settings = db.AppSettings.Single();
        settings.OrganizeCompletedBySite         = true;
        settings.CompletedDownloadsTargetFolder  = targetFolder;

        var site = new PrdbSite
        {
            Id           = Guid.NewGuid(),
            Title        = "Alpha Site",
            Url          = "https://alpha.example",
            SyncedAtUtc  = DateTime.UtcNow,
        };

        var video = new PrdbVideo
        {
            Id                = Guid.NewGuid(),
            Title             = "Some Scene Title",
            SiteId            = site.Id,
            Site              = site,
            PrdbCreatedAtUtc  = DateTime.UtcNow,
            PrdbUpdatedAtUtc  = DateTime.UtcNow,
            SyncedAtUtc       = DateTime.UtcNow,
        };

        var prename = new PrdbPreDbEntry
        {
            Id           = Guid.NewGuid(),
            Title        = "Some.Scene.Title.1080p",
            PrdbVideoId  = video.Id,
            CreatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc  = DateTime.UtcNow,
        };

        var indexer = new Indexer
        {
            Id           = Guid.NewGuid(),
            Title        = "Test Indexer",
            Url          = "https://indexer.example",
            ParsingType  = ParsingType.Newznab,
            IsEnabled    = true,
        };

        var indexerRow = new IndexerRow
        {
            Id      = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Indexer = indexer,
            Title   = "Some.Scene.Title.1080p",
            NzbId   = "abc001",
            NzbUrl  = "https://indexer.example/nzb/abc001",
            NzbSize = 1_500_000_000,
            Category = 7020,
        };

        var match = new IndexerRowMatch
        {
            Id                   = Guid.NewGuid(),
            IndexerRowId         = indexerRow.Id,
            IndexerRow           = indexerRow,
            PrdbVideoId          = video.Id,
            Video                = video,
            MatchedPreDbEntryId  = prename.Id,
            MatchedPreDbEntry    = prename,
            MatchedTitle         = "Some.Scene.Title.1080p",
            MatchedAtUtc         = DateTime.UtcNow,
        };

        var downloadClient = new DownloadClient
        {
            Id         = Guid.NewGuid(),
            Title      = "Test SABnzbd",
            ClientType = ClientType.Sabnzbd,
            Host       = "localhost",
            Port       = 8080,
            IsEnabled  = true,
        };

        db.PrdbSites.Add(site);
        db.PrdbVideos.Add(video);
        db.PrdbPreDbEntries.Add(prename);
        db.Indexers.Add(indexer);
        db.IndexerRows.Add(indexerRow);
        db.IndexerRowMatches.Add(match);
        db.DownloadClients.Add(downloadClient);

        await db.SaveChangesAsync();
    }

    private sealed class RescuePreviewDto
    {
        public List<RescuePreviewItemDto> Items { get; set; } = [];
    }

    private sealed class RescuePreviewItemDto
    {
        public string Name { get; set; } = string.Empty;
        public bool IsMatched { get; set; }
        public string? VideoTitle { get; set; }
        public string? SiteTitle { get; set; }
        public int VideoFileCount { get; set; }
    }
}
