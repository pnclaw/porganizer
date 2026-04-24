using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Database;

namespace porganizer.Api.Tests.Prdb;

public sealed class PrdbVideosTests : IAsyncLifetime
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

        db.PrdbSites.AddRange(matchingSite, otherSite);
        db.PrdbVideos.AddRange(
            new PrdbVideo
            {
                Id = Guid.NewGuid(),
                Title = "Alpha Scene",
                SiteId = matchingSite.Id,
                Site = matchingSite,
                ReleaseDate = new DateOnly(2026, 4, 1),
                PrdbCreatedAtUtc = DateTime.UtcNow,
                PrdbUpdatedAtUtc = DateTime.UtcNow,
                SyncedAtUtc = DateTime.UtcNow,
            },
            new PrdbVideo
            {
                Id = Guid.NewGuid(),
                Title = "Delta Scene",
                SiteId = otherSite.Id,
                Site = otherSite,
                ReleaseDate = new DateOnly(2026, 4, 2),
                PrdbCreatedAtUtc = DateTime.UtcNow,
                PrdbUpdatedAtUtc = DateTime.UtcNow,
                SyncedAtUtc = DateTime.UtcNow,
            });

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
}
