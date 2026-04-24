using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Database;

namespace porganizer.Api.Tests.Prdb;

public sealed class PrdbPreDbTests : IAsyncLifetime
{
    private readonly PorganizerApiFactory _factory = new();
    private HttpClient _client = null!;

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
    public async Task GetAll_DefaultsToAllLatestEntries()
    {
        var response = await _client.GetAsync("/api/prdb-predb");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<PrdbPreDbListItemDto>>();

        body.Should().NotBeNull();
        body!.Total.Should().Be(3);
        body.Items.Should().HaveCount(3);
        body.Items.Select(i => i.Title).Should().ContainInOrder("Zulu.Release", "Orphan.Release", "Alpha.Release");
    }

    [Fact]
    public async Task GetAll_CanFilterUnlinkedAndSearchAcrossVideoTitle()
    {
        var response = await _client.GetAsync("/api/prdb-predb?hasLinkedVideo=false&search=orphan");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<PrdbPreDbListItemDto>>();

        body.Should().NotBeNull();
        body!.Total.Should().Be(1);
        body.Items.Should().ContainSingle();
        body.Items[0].Title.Should().Be("Orphan.Release");
        body.Items[0].HasLinkedVideo.Should().BeFalse();
    }

    [Fact]
    public async Task FilterOptions_ReturnDistinctSites()
    {
        var response = await _client.GetAsync("/api/prdb-predb/filter-options");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrdbPreDbFilterOptionsDto>();

        body.Should().NotBeNull();
        body!.Sites.Select(s => s.Title).Should().Equal("Site A", "Site B");
    }

    private async Task SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var siteA = new PrdbSite
        {
            Id = Guid.NewGuid(),
            Title = "Site A",
            Url = "https://a.example",
            SyncedAtUtc = DateTime.UtcNow,
        };
        var siteB = new PrdbSite
        {
            Id = Guid.NewGuid(),
            Title = "Site B",
            Url = "https://b.example",
            SyncedAtUtc = DateTime.UtcNow,
        };

        var linkedVideo = new PrdbVideo
        {
            Id = Guid.NewGuid(),
            Title = "Alpha Scene",
            SiteId = siteA.Id,
            Site = siteA,
            ReleaseDate = new DateOnly(2026, 4, 1),
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        };

        var newerLinkedVideo = new PrdbVideo
        {
            Id = Guid.NewGuid(),
            Title = "Zulu Scene",
            SiteId = siteB.Id,
            Site = siteB,
            ReleaseDate = new DateOnly(2026, 4, 3),
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        };

        db.PrdbSites.AddRange(siteA, siteB);
        db.PrdbVideos.AddRange(linkedVideo, newerLinkedVideo);
        db.PrdbPreDbEntries.AddRange(
            new PrdbPreDbEntry
            {
                Id = Guid.NewGuid(),
                Title = "Alpha.Release",
                CreatedAtUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                PrdbVideoId = linkedVideo.Id,
                PrdbSiteId = siteA.Id,
                VideoTitle = linkedVideo.Title,
                SiteTitle = siteA.Title,
                ReleaseDate = linkedVideo.ReleaseDate,
                SyncedAtUtc = DateTime.UtcNow,
            },
            new PrdbPreDbEntry
            {
                Id = Guid.NewGuid(),
                Title = "Zulu.Release",
                CreatedAtUtc = new DateTime(2026, 4, 4, 9, 0, 0, DateTimeKind.Utc),
                PrdbVideoId = newerLinkedVideo.Id,
                PrdbSiteId = siteB.Id,
                VideoTitle = newerLinkedVideo.Title,
                SiteTitle = siteB.Title,
                ReleaseDate = newerLinkedVideo.ReleaseDate,
                SyncedAtUtc = DateTime.UtcNow,
            },
            new PrdbPreDbEntry
            {
                Id = Guid.NewGuid(),
                Title = "Orphan.Release",
                CreatedAtUtc = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc),
                PrdbVideoId = null,
                PrdbSiteId = siteA.Id,
                VideoTitle = "Orphan Clip",
                SiteTitle = siteA.Title,
                ReleaseDate = new DateOnly(2026, 4, 2),
                SyncedAtUtc = DateTime.UtcNow,
            });

        await db.SaveChangesAsync();
    }

    private sealed class PagedResult<T>
    {
        public List<T> Items { get; set; } = [];
        public int Total { get; set; }
    }

    private sealed class PrdbPreDbListItemDto
    {
        public string Title { get; set; } = string.Empty;
        public bool HasLinkedVideo { get; set; }
    }

    private sealed class PrdbPreDbFilterOptionsDto
    {
        public List<SiteDto> Sites { get; set; } = [];
    }

    private sealed class SiteDto
    {
        public string Title { get; set; } = string.Empty;
    }
}
