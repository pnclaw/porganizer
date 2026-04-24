using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using porganizer.Api.Features.Prdb.Sync;
using porganizer.Database;

namespace porganizer.Api.Tests.Prdb;

public sealed class PrdbVideoUserImageSyncServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SqliteConnection _connection;

    public PrdbVideoUserImageSyncServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task RunAsync_FirstRun_StartsFromEpochAndSetsCursor()
    {
        var videoId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var serverNow = new DateTime(2026, 4, 17, 10, 0, 0, DateTimeKind.Utc);

        _db.PrdbSites.Add(new PrdbSite { Id = Guid.NewGuid(), Title = "S", Url = "u", SyncedAtUtc = DateTime.UtcNow });
        _db.PrdbVideos.Add(new PrdbVideo { Id = videoId, SiteId = _db.PrdbSites.Local.First().Id, Title = "V", PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow });
        _db.SaveChanges();

        HttpRequestMessage? capturedRequest = null;

        var httpClient = new HttpClient(new StubHttpMessageHandler(req =>
        {
            capturedRequest = req;
            if (req.RequestUri!.PathAndQuery.Contains("video-user-images/changes"))
            {
                var items = new[]
                {
                    new
                    {
                        eventType = "Created",
                        videoUserImage = new
                        {
                            id = imageId,
                            videoId,
                            previewImageType = "Single",
                            displayOrder = 0,
                            url = "https://cdn.example.com/img.jpg",
                            moderationVisibility = "Public",
                            isDeleted = false,
                            deletedAtUtc = (DateTime?)null,
                            createdAtUtc = serverNow,
                            updatedAtUtc = serverNow,
                        },
                    },
                };
                var body = JsonSerializer.Serialize(new
                {
                    items,
                    pageSize = 1000,
                    hasMore = false,
                    nextCursor = new { updatedAtUtc = serverNow, id = imageId },
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/"),
        };

        var service = new PrdbVideoUserImageSyncService(_db, new StubHttpClientFactory(httpClient), NullLogger<PrdbVideoUserImageSyncService>.Instance);
        var count = await service.RunAsync(CancellationToken.None);

        count.Should().Be(1);

        var image = await _db.PrdbVideoUserImages.SingleAsync(i => i.Id == imageId);
        image.VideoId.Should().Be(videoId);
        image.Url.Should().Be("https://cdn.example.com/img.jpg");
        image.PreviewImageType.Should().Be("Single");
        image.DisplayOrder.Should().Be(0);
        image.ModerationVisibility.Should().Be("Public");

        var settings = await _db.AppSettings.SingleAsync();
        settings.PrdbVideoUserImageSyncCursorUtc.Should().Be(serverNow);
        settings.PrdbVideoUserImageSyncCursorId.Should().Be(imageId);

        capturedRequest!.RequestUri!.Query.Should().Contain("Since=");
    }

    [Fact]
    public async Task RunAsync_IncrementalSync_UpsertDeleteAndAdvancesCursor()
    {
        var videoId = Guid.NewGuid();
        var existingId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var initialCursor = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc);
        var nextCursor = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc);

        _db.PrdbSites.Add(new PrdbSite { Id = Guid.NewGuid(), Title = "S", Url = "u", SyncedAtUtc = DateTime.UtcNow });
        _db.PrdbVideos.Add(new PrdbVideo { Id = videoId, SiteId = _db.PrdbSites.Local.First().Id, Title = "V", PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow });
        _db.PrdbVideoUserImages.AddRange(
            new PrdbVideoUserImage { Id = existingId, VideoId = videoId, Url = "https://old.url/img.jpg", PreviewImageType = "Single", DisplayOrder = 0, ModerationVisibility = "Public", PrdbUpdatedAtUtc = initialCursor, SyncedAtUtc = initialCursor },
            new PrdbVideoUserImage { Id = deletedId, VideoId = videoId, Url = "https://deleted.url/img.jpg", PreviewImageType = "Single", DisplayOrder = 1, ModerationVisibility = "Public", PrdbUpdatedAtUtc = initialCursor, SyncedAtUtc = initialCursor });
        var settings = await _db.AppSettings.SingleAsync();
        settings.PrdbVideoUserImageSyncCursorUtc = initialCursor;
        settings.PrdbVideoUserImageSyncCursorId = existingId;
        _db.SaveChanges();

        var httpClient = new HttpClient(new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("video-user-images/changes"))
            {
                var items = new object[]
                {
                    new
                    {
                        eventType = "Updated",
                        videoUserImage = new
                        {
                            id = existingId,
                            videoId,
                            previewImageType = "Single",
                            displayOrder = 0,
                            url = "https://new.url/img.jpg",
                            moderationVisibility = "Public",
                            isDeleted = false,
                            deletedAtUtc = (DateTime?)null,
                            createdAtUtc = initialCursor,
                            updatedAtUtc = nextCursor,
                        },
                    },
                    new
                    {
                        eventType = "Deleted",
                        videoUserImage = new
                        {
                            id = deletedId,
                            videoId,
                            previewImageType = "Single",
                            displayOrder = 1,
                            url = "https://deleted.url/img.jpg",
                            moderationVisibility = "Public",
                            isDeleted = true,
                            deletedAtUtc = (DateTime?)nextCursor,
                            createdAtUtc = initialCursor,
                            updatedAtUtc = nextCursor,
                        },
                    },
                };
                var body = JsonSerializer.Serialize(new
                {
                    items,
                    pageSize = 1000,
                    hasMore = false,
                    nextCursor = new { updatedAtUtc = nextCursor, id = existingId },
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/"),
        };

        var service = new PrdbVideoUserImageSyncService(_db, new StubHttpClientFactory(httpClient), NullLogger<PrdbVideoUserImageSyncService>.Instance);
        var count = await service.RunAsync(CancellationToken.None);

        count.Should().Be(2);

        var updated = await _db.PrdbVideoUserImages.SingleAsync(i => i.Id == existingId);
        updated.Url.Should().Be("https://new.url/img.jpg");

        var deletedRow = await _db.PrdbVideoUserImages.FirstOrDefaultAsync(i => i.Id == deletedId);
        deletedRow.Should().BeNull();

        var updatedSettings = await _db.AppSettings.SingleAsync();
        updatedSettings.PrdbVideoUserImageSyncCursorUtc.Should().Be(nextCursor);
        updatedSettings.PrdbVideoUserImageSyncCursorId.Should().Be(existingId);
    }

    [Fact]
    public async Task RunAsync_SkipsImagesForUnknownVideos()
    {
        var knownVideoId = Guid.NewGuid();
        var unknownVideoId = Guid.NewGuid();
        var knownImageId = Guid.NewGuid();
        var unknownImageId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 17, 10, 0, 0, DateTimeKind.Utc);

        _db.PrdbSites.Add(new PrdbSite { Id = Guid.NewGuid(), Title = "S", Url = "u", SyncedAtUtc = DateTime.UtcNow });
        _db.PrdbVideos.Add(new PrdbVideo { Id = knownVideoId, SiteId = _db.PrdbSites.Local.First().Id, Title = "V", PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow });
        _db.SaveChanges();

        var httpClient = new HttpClient(new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("video-user-images/changes"))
            {
                var items = new[]
                {
                    new { eventType = "Created", videoUserImage = new { id = knownImageId, videoId = knownVideoId, previewImageType = "Single", displayOrder = 0, url = "https://cdn.example.com/known.jpg", moderationVisibility = "Public", isDeleted = false, deletedAtUtc = (DateTime?)null, createdAtUtc = now, updatedAtUtc = now } },
                    new { eventType = "Created", videoUserImage = new { id = unknownImageId, videoId = unknownVideoId, previewImageType = "Single", displayOrder = 0, url = "https://cdn.example.com/unknown.jpg", moderationVisibility = "Public", isDeleted = false, deletedAtUtc = (DateTime?)null, createdAtUtc = now, updatedAtUtc = now } },
                };
                var body = JsonSerializer.Serialize(new { items, pageSize = 1000, hasMore = false, nextCursor = new { updatedAtUtc = now, id = knownImageId } });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/"),
        };

        var service = new PrdbVideoUserImageSyncService(_db, new StubHttpClientFactory(httpClient), NullLogger<PrdbVideoUserImageSyncService>.Instance);
        await service.RunAsync(CancellationToken.None);

        (await _db.PrdbVideoUserImages.CountAsync()).Should().Be(1);
        (await _db.PrdbVideoUserImages.AnyAsync(i => i.Id == knownImageId)).Should().BeTrue();
        (await _db.PrdbVideoUserImages.AnyAsync(i => i.Id == unknownImageId)).Should().BeFalse();
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
