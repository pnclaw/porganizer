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

public sealed class PrdbWantedVideoSyncServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SqliteConnection _connection;

    public PrdbWantedVideoSyncServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        if (!_db.AppSettings.Any())
        {
            _db.AppSettings.Add(new AppSettings { Id = 1 });
            _db.SaveChanges();
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task RunAsync_ChangeFeed_UpsertsDeletesCreatesStubsAndAdvancesCursor()
    {
        var existingVideoId = Guid.NewGuid();
        var deletedVideoId = Guid.NewGuid();
        var newVideoId = Guid.NewGuid();
        var existingSiteId = Guid.NewGuid();
        var newSiteId = Guid.NewGuid();
        var initialCursorAt = new DateTime(2026, 4, 8, 10, 0, 0, DateTimeKind.Utc);
        var initialCursorId = Guid.NewGuid();
        var nextCursorId = newVideoId;

        _db.PrdbSites.Add(new PrdbSite
        {
            Id = existingSiteId,
            Title = "Existing Site",
            Url = "https://example.test/existing",
            SyncedAtUtc = initialCursorAt.AddDays(-10),
        });

        _db.PrdbVideos.AddRange(
            new PrdbVideo
            {
                Id = existingVideoId,
                SiteId = existingSiteId,
                Title = "Existing Video",
                PrdbCreatedAtUtc = initialCursorAt.AddDays(-10),
                PrdbUpdatedAtUtc = initialCursorAt.AddDays(-10),
                SyncedAtUtc = initialCursorAt.AddDays(-10),
            },
            new PrdbVideo
            {
                Id = deletedVideoId,
                SiteId = existingSiteId,
                Title = "Deleted Video",
                PrdbCreatedAtUtc = initialCursorAt.AddDays(-10),
                PrdbUpdatedAtUtc = initialCursorAt.AddDays(-10),
                SyncedAtUtc = initialCursorAt.AddDays(-10),
            });

        _db.PrdbWantedVideos.AddRange(
            new PrdbWantedVideo
            {
                VideoId = existingVideoId,
                IsFulfilled = true,
                FulfilledAtUtc = initialCursorAt.AddHours(-1),
                FulfilledInQuality = 2,
                FulfillmentExternalId = "local-fulfillment",
                FulfillmentByApp = 1,
                PrdbCreatedAtUtc = initialCursorAt.AddDays(-2),
                PrdbUpdatedAtUtc = initialCursorAt.AddMinutes(-10),
                SyncedAtUtc = initialCursorAt.AddMinutes(-10),
            },
            new PrdbWantedVideo
            {
                VideoId = deletedVideoId,
                IsFulfilled = false,
                PrdbCreatedAtUtc = initialCursorAt.AddDays(-2),
                PrdbUpdatedAtUtc = initialCursorAt.AddMinutes(-20),
                SyncedAtUtc = initialCursorAt.AddMinutes(-20),
            });

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbWantedVideoSyncCursorUtc = initialCursorAt;
        settings.PrdbWantedVideoSyncCursorId = initialCursorId;
        await _db.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new
                {
                    eventType = "updated",
                    wantedVideo = new
                    {
                        videoId = existingVideoId,
                        videoTitle = "Existing Video",
                        siteTitle = "Existing Site",
                        videoReleaseDate = "2026-04-01",
                        videoCreatedAtUtc = "2026-04-01T00:00:00Z",
                        imageCdnPath = (string?)null,
                        isDeleted = false,
                        deletedAtUtc = (string?)null,
                        isFulfilled = false,
                        fulfilledAtUtc = (string?)null,
                        fulfilledInQuality = (int?)null,
                        fulfillmentExternalId = (string?)null,
                        fulfillmentByApp = (int?)null,
                        createdAtUtc = "2026-04-06T10:00:00Z",
                        updatedAtUtc = "2026-04-08T10:05:00Z"
                    }
                },
                new
                {
                    eventType = "deleted",
                    wantedVideo = new
                    {
                        videoId = deletedVideoId,
                        videoTitle = "Deleted Video",
                        siteTitle = "Existing Site",
                        videoReleaseDate = "2026-03-15",
                        videoCreatedAtUtc = "2026-03-15T00:00:00Z",
                        imageCdnPath = (string?)null,
                        isDeleted = true,
                        deletedAtUtc = "2026-04-08T10:06:00Z",
                        isFulfilled = false,
                        fulfilledAtUtc = (string?)null,
                        fulfilledInQuality = (int?)null,
                        fulfillmentExternalId = (string?)null,
                        fulfillmentByApp = (int?)null,
                        createdAtUtc = "2026-04-05T10:00:00Z",
                        updatedAtUtc = "2026-04-08T10:06:00Z"
                    }
                },
                new
                {
                    eventType = "created",
                    wantedVideo = new
                    {
                        videoId = newVideoId,
                        videoTitle = "New Video",
                        siteTitle = "New Site",
                        videoReleaseDate = "2026-04-08",
                        videoCreatedAtUtc = "2026-04-08T10:07:00Z",
                        imageCdnPath = "/img/new.jpg",
                        isDeleted = false,
                        deletedAtUtc = (string?)null,
                        isFulfilled = false,
                        fulfilledAtUtc = (string?)null,
                        fulfilledInQuality = (int?)null,
                        fulfillmentExternalId = (string?)null,
                        fulfillmentByApp = (int?)null,
                        createdAtUtc = "2026-04-08T10:07:00Z",
                        updatedAtUtc = "2026-04-08T10:07:00Z"
                    }
                }
            },
            pageSize = 1000,
            hasMore = false,
            nextCursor = new
            {
                updatedAtUtc = "2026-04-08T10:07:00Z",
                id = nextCursorId
            }
        });

        HttpRequestMessage? changesRequest = null;
        var detailRequests = new List<string>();

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/wanted-videos/changes", StringComparison.Ordinal))
            {
                changesRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
            }

            if (request.RequestUri.AbsolutePath.EndsWith($"/videos/{newVideoId}", StringComparison.Ordinal))
            {
                detailRequests.Add(request.RequestUri.PathAndQuery);
                var detailPayload = JsonSerializer.Serialize(new
                {
                    id = newVideoId,
                    title = "New Video",
                    releaseDate = "2026-04-08",
                    createdAtUtc = "2026-04-08T10:07:00Z",
                    updatedAtUtc = "2026-04-08T10:07:00Z",
                    site = new
                    {
                        id = newSiteId,
                        title = "New Site",
                        url = "https://example.test/new"
                    },
                    images = Array.Empty<object>(),
                    preNames = Array.Empty<object>(),
                    actors = Array.Empty<object>()
                });

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(detailPayload, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = new PrdbWantedVideoSyncService(
            _db,
            new StubHttpClientFactory(httpClient),
            NullLogger<PrdbWantedVideoSyncService>.Instance);

        await service.RunAsync(CancellationToken.None);

        changesRequest.Should().NotBeNull();
        changesRequest!.RequestUri!.Query.Should().Contain($"Since={Uri.EscapeDataString(initialCursorAt.ToString("O"))}");
        changesRequest.RequestUri.Query.Should().Contain($"SinceId={initialCursorId}");
        detailRequests.Should().ContainSingle();

        var existing = await _db.PrdbWantedVideos.SingleAsync(w => w.VideoId == existingVideoId);
        existing.IsFulfilled.Should().BeTrue();
        existing.FulfillmentExternalId.Should().Be("local-fulfillment");
        existing.PrdbUpdatedAtUtc.Should().Be(new DateTime(2026, 4, 8, 10, 5, 0, DateTimeKind.Utc));

        (await _db.PrdbWantedVideos.AnyAsync(w => w.VideoId == deletedVideoId)).Should().BeFalse();

        var created = await _db.PrdbWantedVideos.SingleAsync(w => w.VideoId == newVideoId);
        created.IsFulfilled.Should().BeFalse();

        var createdVideo = await _db.PrdbVideos.SingleAsync(v => v.Id == newVideoId);
        createdVideo.SiteId.Should().Be(newSiteId);

        var createdSite = await _db.PrdbSites.SingleAsync(s => s.Id == newSiteId);
        createdSite.Title.Should().Be("New Site");

        settings = await _db.AppSettings.SingleAsync();
        settings.PrdbWantedVideoSyncCursorUtc.Should().Be(new DateTime(2026, 4, 8, 10, 7, 0, DateTimeKind.Utc));
        settings.PrdbWantedVideoSyncCursorId.Should().Be(nextCursorId);
        settings.PrdbWantedVideoLastSyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_InitialEmptyChangeFeed_SeedsCursorFromEpochQuery()
    {
        var beforeRun = DateTime.UtcNow;
        HttpRequestMessage? changesRequest = null;

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbWantedVideoSyncCursorUtc = null;
        settings.PrdbWantedVideoSyncCursorId = null;
        await _db.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new
        {
            items = Array.Empty<object>(),
            pageSize = 1000,
            hasMore = false,
            nextCursor = (object?)null
        });

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            changesRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = new PrdbWantedVideoSyncService(
            _db,
            new StubHttpClientFactory(httpClient),
            NullLogger<PrdbWantedVideoSyncService>.Instance);

        await service.RunAsync(CancellationToken.None);

        changesRequest.Should().NotBeNull();
        changesRequest!.RequestUri!.Query.Should().Contain("PageSize=1000");
        changesRequest.RequestUri.Query.Should().Contain($"Since={Uri.EscapeDataString(DateTime.UnixEpoch.ToString("O"))}");
        changesRequest.RequestUri.Query.Should().NotContain("SinceId=");

        settings = await _db.AppSettings.SingleAsync();
        settings.PrdbWantedVideoSyncCursorUtc.Should().BeOnOrAfter(beforeRun);
        settings.PrdbWantedVideoSyncCursorId.Should().BeNull();
        settings.PrdbWantedVideoLastSyncedAt.Should().BeOnOrAfter(beforeRun);
    }

    [Fact]
    public async Task RunAsync_ChangeFeed_SendsUtcSinceWhenStoredCursorKindIsUnspecified()
    {
        var cursorAt = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Unspecified);
        var cursorId = Guid.NewGuid();

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbWantedVideoSyncCursorUtc = cursorAt;
        settings.PrdbWantedVideoSyncCursorId = cursorId;
        await _db.SaveChangesAsync();

        HttpRequestMessage? changesRequest = null;

        var payload = JsonSerializer.Serialize(new
        {
            items = Array.Empty<object>(),
            pageSize = 1000,
            hasMore = false,
            nextCursor = (object?)null
        });

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/wanted-videos/changes", StringComparison.Ordinal))
            {
                changesRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = new PrdbWantedVideoSyncService(
            _db,
            new StubHttpClientFactory(httpClient),
            NullLogger<PrdbWantedVideoSyncService>.Instance);

        await service.RunAsync(CancellationToken.None);

        changesRequest.Should().NotBeNull();
        changesRequest!.RequestUri!.Query.Should().Contain("Since=2026-04-08T12%3A00%3A00.0000000Z");
        changesRequest.RequestUri.Query.Should().Contain($"SinceId={cursorId}");
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
