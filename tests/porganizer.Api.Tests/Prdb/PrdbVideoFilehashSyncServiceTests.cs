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

public sealed class PrdbVideoFilehashSyncServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SqliteConnection _connection;

    public PrdbVideoFilehashSyncServiceTests()
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
    public async Task RunAsync_IncrementalChangeFeed_UpsertsDeletesAndAdvancesCursor()
    {
        var existingId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var existingVideoId = Guid.NewGuid();
        var deletedVideoId = Guid.NewGuid();
        var newVideoId = Guid.NewGuid();
        var initialCursorAt = new DateTime(2026, 4, 8, 8, 0, 0, DateTimeKind.Utc);
        var existingCursorId = Guid.NewGuid();
        var nextCursorId = Guid.NewGuid();

        _db.PrdbSites.Add(new PrdbSite
        {
            Id = siteId,
            Title = "Test Site",
            Url = "https://example.test/site",
            SyncedAtUtc = initialCursorAt.AddDays(-10),
        });

        _db.PrdbVideos.AddRange(
            new PrdbVideo
            {
                Id = existingVideoId,
                SiteId = siteId,
                Title = "Existing Video",
                PrdbCreatedAtUtc = initialCursorAt.AddDays(-10),
                PrdbUpdatedAtUtc = initialCursorAt.AddDays(-10),
                SyncedAtUtc = initialCursorAt.AddDays(-10),
            },
            new PrdbVideo
            {
                Id = deletedVideoId,
                SiteId = siteId,
                Title = "Deleted Video",
                PrdbCreatedAtUtc = initialCursorAt.AddDays(-10),
                PrdbUpdatedAtUtc = initialCursorAt.AddDays(-10),
                SyncedAtUtc = initialCursorAt.AddDays(-10),
            },
            new PrdbVideo
            {
                Id = newVideoId,
                SiteId = siteId,
                Title = "New Video",
                PrdbCreatedAtUtc = initialCursorAt.AddDays(-10),
                PrdbUpdatedAtUtc = initialCursorAt.AddDays(-10),
                SyncedAtUtc = initialCursorAt.AddDays(-10),
            });

        _db.PrdbVideoFilehashes.AddRange(
            new PrdbVideoFilehash
            {
                Id = existingId,
                VideoId = existingVideoId,
                Filename = "existing-old.mp4",
                OsHash = "AAAA",
                Filesize = 100,
                SubmissionCount = 1,
                IsVerified = false,
                PrdbCreatedAtUtc = initialCursorAt.AddDays(-1),
                PrdbUpdatedAtUtc = initialCursorAt.AddDays(-1),
                SyncedAtUtc = initialCursorAt.AddDays(-1),
            },
            new PrdbVideoFilehash
            {
                Id = deletedId,
                VideoId = deletedVideoId,
                Filename = "deleted.mp4",
                OsHash = "BBBB",
                Filesize = 200,
                SubmissionCount = 2,
                IsVerified = true,
                PrdbCreatedAtUtc = initialCursorAt.AddDays(-2),
                PrdbUpdatedAtUtc = initialCursorAt.AddDays(-2),
                SyncedAtUtc = initialCursorAt.AddDays(-2),
            });

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbFilehashBackfillPage = null;
        settings.PrdbFilehashSyncCursorUtc = initialCursorAt;
        settings.PrdbFilehashSyncCursorId = existingCursorId;
        await _db.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new
                {
                    eventType = "updated",
                    filehash = new
                    {
                        id = existingId,
                        videoId = existingVideoId,
                        filename = "existing-new.mp4",
                        osHash = "CCCC",
                        pHash = (string?)null,
                        filesize = 1234,
                        submissionCount = 5,
                        isVerified = true,
                        isDeleted = false,
                        deletedAtUtc = (string?)null,
                        createdAtUtc = "2026-04-07T08:00:00Z",
                        updatedAtUtc = "2026-04-08T08:05:00Z"
                    }
                },
                new
                {
                    eventType = "deleted",
                    filehash = new
                    {
                        id = deletedId,
                        videoId = (Guid?)null,
                        filename = "deleted.mp4",
                        osHash = (string?)null,
                        pHash = (string?)null,
                        filesize = 200,
                        submissionCount = 2,
                        isVerified = true,
                        isDeleted = true,
                        deletedAtUtc = "2026-04-08T08:06:00Z",
                        createdAtUtc = "2026-04-06T08:00:00Z",
                        updatedAtUtc = "2026-04-08T08:06:00Z"
                    }
                },
                new
                {
                    eventType = "created",
                    filehash = new
                    {
                        id = newId,
                        videoId = newVideoId,
                        filename = "new-file.mp4",
                        osHash = "DDDD",
                        pHash = "EEEE",
                        filesize = 4321,
                        submissionCount = 1,
                        isVerified = false,
                        isDeleted = false,
                        deletedAtUtc = (string?)null,
                        createdAtUtc = "2026-04-08T08:07:00Z",
                        updatedAtUtc = "2026-04-08T08:07:00Z"
                    }
                }
            },
            pageSize = 1000,
            hasMore = false,
            nextCursor = new
            {
                updatedAtUtc = "2026-04-08T08:07:00Z",
                id = nextCursorId
            }
        });

        HttpRequestMessage? capturedRequest = null;
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = new PrdbVideoFilehashSyncService(
            _db,
            new StubHttpClientFactory(httpClient),
            NullLogger<PrdbVideoFilehashSyncService>.Instance);

        await service.RunAsync(CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.Query.Should().Contain($"Since={Uri.EscapeDataString(initialCursorAt.ToString("O"))}");
        capturedRequest.RequestUri!.Query.Should().Contain($"SinceId={existingCursorId}");

        var existing = await _db.PrdbVideoFilehashes.SingleAsync(f => f.Id == existingId);
        existing.Filename.Should().Be("existing-new.mp4");
        existing.OsHash.Should().Be("CCCC");
        existing.Filesize.Should().Be(1234);
        existing.SubmissionCount.Should().Be(5);
        existing.IsVerified.Should().BeTrue();

        (await _db.PrdbVideoFilehashes.AnyAsync(f => f.Id == deletedId)).Should().BeFalse();

        var created = await _db.PrdbVideoFilehashes.SingleAsync(f => f.Id == newId);
        created.VideoId.Should().Be(newVideoId);
        created.PHash.Should().Be("EEEE");

        settings = await _db.AppSettings.SingleAsync();
        settings.PrdbFilehashSyncCursorUtc.Should().Be(new DateTime(2026, 4, 8, 8, 7, 0, DateTimeKind.Utc));
        settings.PrdbFilehashSyncCursorId.Should().Be(nextCursorId);
    }

    [Fact]
    public async Task RunAsync_IncrementalChangeFeed_WithEmptyPage_KeepsExistingCursor()
    {
        var initialCursorAt = new DateTime(2026, 4, 8, 8, 0, 0, DateTimeKind.Utc);
        var initialCursorId = Guid.NewGuid();

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbFilehashBackfillPage = null;
        settings.PrdbFilehashSyncCursorUtc = initialCursorAt;
        settings.PrdbFilehashSyncCursorId = initialCursorId;
        await _db.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new
        {
            items = Array.Empty<object>(),
            pageSize = 1000,
            hasMore = false,
            nextCursor = (object?)null
        });

        var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = new PrdbVideoFilehashSyncService(
            _db,
            new StubHttpClientFactory(httpClient),
            NullLogger<PrdbVideoFilehashSyncService>.Instance);

        await service.RunAsync(CancellationToken.None);

        settings = await _db.AppSettings.SingleAsync();
        settings.PrdbFilehashSyncCursorUtc.Should().Be(initialCursorAt);
        settings.PrdbFilehashSyncCursorId.Should().Be(initialCursorId);
    }

    [Fact]
    public async Task RunAsync_IncrementalChangeFeed_SendsUtcSinceWhenStoredCursorKindIsUnspecified()
    {
        var cursorAt = new DateTime(2026, 4, 8, 8, 0, 0, DateTimeKind.Unspecified);
        var cursorId = Guid.NewGuid();

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbFilehashBackfillPage = null;
        settings.PrdbFilehashSyncCursorUtc = cursorAt;
        settings.PrdbFilehashSyncCursorId = cursorId;
        await _db.SaveChangesAsync();

        HttpRequestMessage? capturedRequest = null;
        var payload = JsonSerializer.Serialize(new
        {
            items = Array.Empty<object>(),
            pageSize = 1000,
            hasMore = false,
            nextCursor = (object?)null
        });

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = new PrdbVideoFilehashSyncService(
            _db,
            new StubHttpClientFactory(httpClient),
            NullLogger<PrdbVideoFilehashSyncService>.Instance);

        await service.RunAsync(CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.Query.Should().Contain("Since=2026-04-08T08%3A00%3A00.0000000Z");
        capturedRequest.RequestUri.Query.Should().Contain($"SinceId={cursorId}");
    }

    [Fact]
    public async Task RunAsync_IncrementalChangeFeed_WithNullNextCursor_AdvancesCursorToLastItem()
    {
        var filehashId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        var initialCursorAt = new DateTime(2026, 4, 8, 8, 0, 0, DateTimeKind.Utc);
        var initialCursorId = Guid.NewGuid();
        var expectedCursorAt = new DateTime(2026, 4, 8, 8, 10, 0, DateTimeKind.Utc);

        _db.PrdbSites.Add(new PrdbSite
        {
            Id = siteId,
            Title = "Test Site",
            Url = "https://example.test/site",
            SyncedAtUtc = initialCursorAt.AddDays(-10),
        });

        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id = videoId,
            SiteId = siteId,
            Title = "Test Video",
            PrdbCreatedAtUtc = initialCursorAt.AddDays(-10),
            PrdbUpdatedAtUtc = initialCursorAt.AddDays(-10),
            SyncedAtUtc = initialCursorAt.AddDays(-10),
        });

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbFilehashBackfillPage = null;
        settings.PrdbFilehashSyncCursorUtc = initialCursorAt;
        settings.PrdbFilehashSyncCursorId = initialCursorId;
        await _db.SaveChangesAsync();

        // Simulate API returning items but no NextCursor (was the bug: cursor would not advance)
        var payload = JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new
                {
                    eventType = "created",
                    filehash = new
                    {
                        id = filehashId,
                        videoId,
                        filename = "new-file.mp4",
                        osHash = "ZZZZ",
                        pHash = (string?)null,
                        filesize = 500,
                        submissionCount = 1,
                        isVerified = false,
                        isDeleted = false,
                        deletedAtUtc = (string?)null,
                        createdAtUtc = "2026-04-08T08:09:00Z",
                        updatedAtUtc = "2026-04-08T08:10:00Z"
                    }
                }
            },
            pageSize = 1000,
            hasMore = false,
            nextCursor = (object?)null    // ← null cursor: this was the bug trigger
        });

        var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = new PrdbVideoFilehashSyncService(
            _db,
            new StubHttpClientFactory(httpClient),
            NullLogger<PrdbVideoFilehashSyncService>.Instance);

        await service.RunAsync(CancellationToken.None);

        var stored = await _db.PrdbVideoFilehashes.SingleAsync(f => f.Id == filehashId);
        stored.OsHash.Should().Be("ZZZZ");

        settings = await _db.AppSettings.SingleAsync();
        settings.PrdbFilehashSyncCursorUtc.Should().Be(expectedCursorAt);
        settings.PrdbFilehashSyncCursorId.Should().Be(filehashId);
    }

    [Fact]
    public async Task RunAsync_WhenChangesEndpointReturns404_FallsBackToLatestEndpoint()
    {
        var filehashId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        var initialCursorAt = new DateTime(2026, 4, 8, 9, 0, 0, DateTimeKind.Utc);
        var initialCursorId = Guid.NewGuid();
        var beforeRun = DateTime.UtcNow;
        var requestedPaths = new List<string>();

        _db.PrdbSites.Add(new PrdbSite
        {
            Id = siteId,
            Title = "Fallback Site",
            Url = "https://example.test/fallback",
            SyncedAtUtc = initialCursorAt.AddDays(-10),
        });

        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id = videoId,
            SiteId = siteId,
            Title = "Fallback Video",
            PrdbCreatedAtUtc = initialCursorAt.AddDays(-10),
            PrdbUpdatedAtUtc = initialCursorAt.AddDays(-10),
            SyncedAtUtc = initialCursorAt.AddDays(-10),
        });

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbFilehashBackfillPage = null;
        settings.PrdbFilehashSyncCursorUtc = initialCursorAt;
        settings.PrdbFilehashSyncCursorId = initialCursorId;
        await _db.SaveChangesAsync();

        var latestPayload = JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new
                {
                    id = filehashId,
                    videoId,
                    filename = "fallback.mp4",
                    osHash = "FFFF",
                    pHash = (string?)null,
                    filesize = 900,
                    submissionCount = 2,
                    isVerified = true,
                    createdAtUtc = "2026-04-08T09:01:00Z",
                    updatedAtUtc = "2026-04-08T09:02:00Z"
                }
            },
            totalCount = 1,
            page = 1,
            pageSize = 100
        });

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.PathAndQuery);

            if (request.RequestUri!.AbsolutePath.EndsWith("/videos/filehashes/changes", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            if (request.RequestUri.AbsolutePath.EndsWith("/videos/filehashes/latest", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(latestPayload, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = new PrdbVideoFilehashSyncService(
            _db,
            new StubHttpClientFactory(httpClient),
            NullLogger<PrdbVideoFilehashSyncService>.Instance);

        await service.RunAsync(CancellationToken.None);

        requestedPaths.Should().Contain(p => p.StartsWith("/videos/filehashes/changes?", StringComparison.Ordinal));
        requestedPaths.Should().Contain(p => p.StartsWith("/videos/filehashes/latest?", StringComparison.Ordinal));

        var stored = await _db.PrdbVideoFilehashes.SingleAsync(f => f.Id == filehashId);
        stored.VideoId.Should().Be(videoId);
        stored.OsHash.Should().Be("FFFF");

        settings = await _db.AppSettings.SingleAsync();
        settings.PrdbFilehashSyncCursorUtc.Should().BeOnOrAfter(beforeRun);
        settings.PrdbFilehashSyncCursorId.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_BackfillCompletion_SetsCursorBeforeBackfillStartedNotAfter()
    {
        var filehashId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var videoId = Guid.NewGuid();

        _db.PrdbSites.Add(new PrdbSite
        {
            Id = siteId,
            Title = "Backfill Site",
            Url = "https://example.test/backfill",
            SyncedAtUtc = DateTime.UtcNow.AddDays(-10),
        });

        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id = videoId,
            SiteId = siteId,
            Title = "Backfill Video",
            PrdbCreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            PrdbUpdatedAtUtc = DateTime.UtcNow.AddDays(-10),
            SyncedAtUtc = DateTime.UtcNow.AddDays(-10),
        });

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbFilehashBackfillPage = 1;
        settings.PrdbFilehashSyncCursorUtc = null;
        await _db.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new
                {
                    id = filehashId,
                    videoId,
                    filename = "backfill.mp4",
                    osHash = "CCCC",
                    pHash = (string?)null,
                    filesize = 500,
                    submissionCount = 1,
                    isVerified = false,
                    createdAtUtc = "2026-04-01T00:00:00Z",
                    updatedAtUtc = "2026-04-01T00:00:00Z"
                }
            },
            totalCount = 1,
            page = 1,
            pageSize = 100
        });

        var beforeRun = DateTime.UtcNow;

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/videos/filehashes/latest", StringComparison.Ordinal))
            {
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

        var service = new PrdbVideoFilehashSyncService(
            _db,
            new StubHttpClientFactory(httpClient),
            NullLogger<PrdbVideoFilehashSyncService>.Instance);

        await service.RunAsync(CancellationToken.None);

        settings = await _db.AppSettings.SingleAsync();

        // Backfill is done
        settings.PrdbFilehashBackfillPage.Should().BeNull();

        // Cursor must be set to before the backfill started (not after it completed),
        // so that the incremental feed re-checks records updated during the backfill window.
        settings.PrdbFilehashSyncCursorUtc.Should().NotBeNull();
        settings.PrdbFilehashSyncCursorUtc!.Value.Should().BeBefore(beforeRun);

        var stored = await _db.PrdbVideoFilehashes.SingleAsync(f => f.Id == filehashId);
        stored.OsHash.Should().Be("CCCC");
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
