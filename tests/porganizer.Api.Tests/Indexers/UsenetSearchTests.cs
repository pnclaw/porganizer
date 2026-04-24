using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Api.Features.Indexers.Search;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.Indexers;

public sealed class UsenetSearchTests : IAsyncLifetime
{
    private readonly PorganizerApiFactory _factory = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ── GET /api/usenet-search ───────────────────────────────────────────────

    [Fact]
    public async Task Search_ReturnsOkWithEmptyResult_WhenNoRows()
    {
        var response = await _client.GetAsync("/api/usenet-search");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UsenetSearchResponse>();
        body.Should().NotBeNull();
        body!.Total.Should().Be(0);
        body.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_ReturnsRows_WithHints()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "DrunkenSlug",
            Url = "https://drunkenslug.com",
            ParsingType = ParsingType.Newznab,
            IsEnabled = true,
            ApiKey = "key",
            ApiPath = "/api",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Indexers.Add(indexer);

        var row = new IndexerRow
        {
            Id = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Title = "Some.Video.S01E01.1080p",
            NzbId = "abc123",
            NzbUrl = "https://drunkenslug.com/getnzb/abc123",
            NzbSize = 1_500_000_000,
            NzbPublishedAt = DateTime.UtcNow,
            FileSize = null,
            Category = 2000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.IndexerRows.Add(row);

        var site = new PrdbSite
        {
            Id = Guid.NewGuid(),
            Title = "TestSite",
            Url = "https://testsite.com",
            SyncedAtUtc = DateTime.UtcNow,
        };
        db.PrdbSites.Add(site);

        var video = new PrdbVideo
        {
            Id = Guid.NewGuid(),
            Title = "Some Video",
            SiteId = site.Id,
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        };
        db.PrdbVideos.Add(video);

        var preDbEntry = new PrdbPreDbEntry
        {
            Id = Guid.NewGuid(),
            PrdbVideoId = video.Id,
            Title = "Some.Video.S01E01.1080p",
            CreatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        };
        db.PrdbPreDbEntries.Add(preDbEntry);

        var match = new IndexerRowMatch
        {
            Id = Guid.NewGuid(),
            IndexerRowId = row.Id,
            PrdbVideoId = video.Id,
            MatchedPreDbEntryId = preDbEntry.Id,
            MatchedTitle = row.Title,
            MatchedAtUtc = DateTime.UtcNow,
        };
        db.IndexerRowMatches.Add(match);

        var userImage = new PrdbVideoUserImage
        {
            Id = Guid.NewGuid(),
            VideoId = video.Id,
            Url = "https://cdn.example.com/preview.jpg",
            PreviewImageType = "Single",
            DisplayOrder = 0,
            ModerationVisibility = "Public",
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        };
        db.PrdbVideoUserImages.Add(userImage);

        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/usenet-search");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UsenetSearchResponse>();
        body.Should().NotBeNull();
        body!.Total.Should().Be(1);
        var item = body.Items.Single();
        item.MatchedVideoId.Should().Be(video.Id);
        item.MatchedVideoTitle.Should().Be("Some Video");
        item.PreviewImageUrl.Should().Be("https://cdn.example.com/preview.jpg");
    }

    [Fact]
    public async Task Search_FallsBackToCdnImage_WhenNoUserImage()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "DrunkenSlug",
            Url = "https://drunkenslug.com",
            ParsingType = ParsingType.Newznab,
            IsEnabled = true,
            ApiKey = "key",
            ApiPath = "/api",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Indexers.Add(indexer);

        var row = new IndexerRow
        {
            Id = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Title = "CDN.Fallback.Test",
            NzbId = "cdn-fallback",
            NzbUrl = "https://drunkenslug.com/getnzb/cdn",
            NzbSize = 1_000_000_000,
            NzbPublishedAt = DateTime.UtcNow,
            Category = 2000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.IndexerRows.Add(row);

        var site = new PrdbSite
        {
            Id = Guid.NewGuid(),
            Title = "TestSite",
            Url = "https://testsite.com",
            SyncedAtUtc = DateTime.UtcNow,
        };
        db.PrdbSites.Add(site);

        var video = new PrdbVideo
        {
            Id = Guid.NewGuid(),
            Title = "CDN Video",
            SiteId = site.Id,
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        };
        db.PrdbVideos.Add(video);

        db.PrdbVideoImages.Add(new PrdbVideoImage
        {
            Id = Guid.NewGuid(),
            VideoId = video.Id,
            CdnPath = "https://cdn.prdb.net/videos/cdn-fallback.jpg",
        });

        var preDbEntry = new PrdbPreDbEntry
        {
            Id = Guid.NewGuid(),
            PrdbVideoId = video.Id,
            Title = "CDN.Fallback.Test",
            CreatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        };
        db.PrdbPreDbEntries.Add(preDbEntry);

        db.IndexerRowMatches.Add(new IndexerRowMatch
        {
            Id = Guid.NewGuid(),
            IndexerRowId = row.Id,
            PrdbVideoId = video.Id,
            MatchedPreDbEntryId = preDbEntry.Id,
            MatchedTitle = row.Title,
            MatchedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/usenet-search");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UsenetSearchResponse>();
        body.Should().NotBeNull();
        var item = body!.Items.First(i => i.Id == row.Id);
        item.PreviewImageUrl.Should().Be("https://cdn.prdb.net/videos/cdn-fallback.jpg");
    }

    [Fact]
    public async Task Search_PreviewMode_OnlyReturnsRowsWithMatchOrFilehash()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "DrunkenSlug",
            Url = "https://drunkenslug.com",
            ParsingType = ParsingType.Newznab,
            IsEnabled = true,
            ApiKey = "key",
            ApiPath = "/api",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Indexers.Add(indexer);

        // Row without any link
        var unlinkedRow = new IndexerRow
        {
            Id = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Title = "Unlinked.NZB",
            NzbId = "unlinked-nzb",
            NzbUrl = "https://drunkenslug.com/getnzb/unlinked",
            NzbSize = 500_000_000,
            NzbPublishedAt = DateTime.UtcNow.AddHours(-1),
            Category = 2000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.IndexerRows.Add(unlinkedRow);

        // Row with a filehash link
        var hashedRow = new IndexerRow
        {
            Id = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Title = "Hashed.NZB",
            NzbId = "hashed-nzb",
            NzbUrl = "https://drunkenslug.com/getnzb/hashed",
            NzbSize = 1_000_000_000,
            NzbPublishedAt = DateTime.UtcNow,
            Category = 2000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.IndexerRows.Add(hashedRow);

        db.PrdbIndexerFilehashes.Add(new PrdbIndexerFilehash
        {
            Id = Guid.NewGuid(),
            IndexerSource = 0, // DrunkenSlug
            IndexerId = hashedRow.NzbId,
            Filename = "Hashed.NZB.mkv",
            OsHash = "aabbccdd",
            Filesize = 1_000_000_000,
            SubmissionCount = 1,
            IsVerified = false,
            IsDeleted = false,
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/usenet-search?previewMode=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UsenetSearchResponse>();
        body.Should().NotBeNull();
        body!.Total.Should().Be(1);
        body.Items.Single().Id.Should().Be(hashedRow.Id);
        body.Items.Single().HasFilehashLink.Should().BeTrue();
    }

    [Fact]
    public async Task Search_FilterByIndexerId_OnlyReturnsMatchingIndexerRows()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var indexerA = new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "IndexerA",
            Url = "https://indexera.com",
            ParsingType = ParsingType.Newznab,
            IsEnabled = true,
            ApiKey = "key",
            ApiPath = "/api",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var indexerB = new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "IndexerB",
            Url = "https://indexerb.com",
            ParsingType = ParsingType.Newznab,
            IsEnabled = true,
            ApiKey = "key",
            ApiPath = "/api",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Indexers.AddRange(indexerA, indexerB);

        db.IndexerRows.Add(new IndexerRow
        {
            Id = Guid.NewGuid(),
            IndexerId = indexerA.Id,
            Title = "Row from A",
            NzbId = "rowA",
            NzbUrl = "https://indexera.com/nzb/rowA",
            NzbSize = 100_000_000,
            Category = 2000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        db.IndexerRows.Add(new IndexerRow
        {
            Id = Guid.NewGuid(),
            IndexerId = indexerB.Id,
            Title = "Row from B",
            NzbId = "rowB",
            NzbUrl = "https://indexerb.com/nzb/rowB",
            NzbSize = 100_000_000,
            Category = 2000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/usenet-search?indexerIds={indexerA.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UsenetSearchResponse>();
        body.Should().NotBeNull();
        body!.Total.Should().Be(1);
        body.Items.Single().IndexerId.Should().Be(indexerA.Id);
    }
}
