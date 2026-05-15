using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.Prdb;

public sealed class PrdbVideosTests : IAsyncLifetime
{
    private readonly PorganizerApiFactory _factory = new();
    private HttpClient _client = null!;

    private Guid _videoId;
    private Guid _indexerTitle1VideoId;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GetAll_CanSearchAcrossSiteTitle()
    {
        var response = await _client.GetAsync("/api/prdb-videos?search=Bravo");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<PrdbVideoListItemDto>>();

        body.Should().NotBeNull();
        body!.Total.Should().Be(1);
        body.Items.Should().ContainSingle();
        body.Items[0].Title.Should().Be("Alpha Scene");
        body.Items[0].SiteTitle.Should().Be("Bravo Network");
    }

    [Fact]
    public async Task GetIndexerMatches_ReturnsNotFound_WhenVideoDoesNotExist()
    {
        var response = await _client.GetAsync($"/api/prdb-videos/{Guid.NewGuid()}/indexer-matches");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetIndexerMatches_ReturnsIndexerTitle()
    {
        var response = await _client.GetAsync($"/api/prdb-videos/{_indexerTitle1VideoId}/indexer-matches");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<VideoIndexerMatchDto>>();

        body.Should().NotBeNull().And.ContainSingle();
        body![0].IndexerTitle.Should().Be("Foxtrot Indexer");
    }

    private async Task SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var matchingSite = new PrdbSite
        {
            Id = Guid.NewGuid(),
            Title = "Bravo Network",
            Url = "https://bravo.example",
            SyncedAtUtc = DateTime.UtcNow,
        };

        var otherSite = new PrdbSite
        {
            Id = Guid.NewGuid(),
            Title = "Charlie Studio",
            Url = "https://charlie.example",
            SyncedAtUtc = DateTime.UtcNow,
        };

        var alphaVideo = new PrdbVideo
        {
            Id = Guid.NewGuid(),
            Title = "Alpha Scene",
            SiteId = matchingSite.Id,
            Site = matchingSite,
            ReleaseDate = new DateOnly(2026, 4, 1),
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        };

        var deltaVideo = new PrdbVideo
        {
            Id = Guid.NewGuid(),
            Title = "Delta Scene",
            SiteId = otherSite.Id,
            Site = otherSite,
            ReleaseDate = new DateOnly(2026, 4, 2),
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        };

        _videoId = alphaVideo.Id;
        _indexerTitle1VideoId = deltaVideo.Id;

        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "Foxtrot Indexer",
            Url = "https://foxtrot.example",
            ParsingType = ParsingType.Newznab,
            IsEnabled = true,
        };

        var indexerRow = new IndexerRow
        {
            Id = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Indexer = indexer,
            Title = "Delta Scene 1080p",
            NzbId = "abc123",
            NzbUrl = "https://foxtrot.example/nzb/abc123",
            NzbSize = 1_000_000,
            Category = 7020,
        };

        var prename = new PrdbPreDbEntry
        {
            Id = Guid.NewGuid(),
            Title = "Delta Scene 1080p",
            PrdbVideoId = deltaVideo.Id,
            CreatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        };

        var match = new IndexerRowMatch
        {
            Id = Guid.NewGuid(),
            IndexerRowId = indexerRow.Id,
            IndexerRow = indexerRow,
            PrdbVideoId = deltaVideo.Id,
            Video = deltaVideo,
            MatchedPreDbEntryId = prename.Id,
            MatchedPreDbEntry = prename,
            MatchedTitle = "Delta Scene 1080p",
            MatchedAtUtc = DateTime.UtcNow,
        };

        db.PrdbSites.AddRange(matchingSite, otherSite);
        db.PrdbVideos.AddRange(alphaVideo, deltaVideo);
        db.Indexers.Add(indexer);
        db.IndexerRows.Add(indexerRow);
        db.PrdbPreDbEntries.Add(prename);
        db.IndexerRowMatches.Add(match);

        await db.SaveChangesAsync();
    }

    private sealed class PagedResult<T>
    {
        public List<T> Items { get; set; } = [];
        public int Total { get; set; }
    }

    private sealed class PrdbVideoListItemDto
    {
        public string Title { get; set; } = string.Empty;
        public string SiteTitle { get; set; } = string.Empty;
    }

    private sealed class VideoIndexerMatchDto
    {
        public string IndexerTitle { get; set; } = string.Empty;
    }
}
