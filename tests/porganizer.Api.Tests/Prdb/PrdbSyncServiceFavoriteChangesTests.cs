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

public sealed class PrdbSyncServiceFavoriteChangesTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SqliteConnection _connection;

    public PrdbSyncServiceFavoriteChangesTests()
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
    public async Task SyncAsync_FavoriteSiteChanges_AppliesCreateDeleteAndAdvancesCursor()
    {
        var existingSiteId = Guid.NewGuid();
        var deletedSiteId = Guid.NewGuid();
        var existingNetworkId = Guid.NewGuid();
        var newSiteId = Guid.NewGuid();
        var newNetworkId = Guid.NewGuid();
        var initialCursorAt = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc);
        var initialCursorId = Guid.NewGuid();

        _db.PrdbNetworks.Add(new PrdbNetwork
        {
            Id = existingNetworkId,
            Title = "Existing Network",
            Url = string.Empty,
            SyncedAtUtc = initialCursorAt.AddDays(-2),
        });

        _db.PrdbSites.AddRange(
            new PrdbSite
            {
                Id = existingSiteId,
                Title = "Existing Site",
                Url = "https://example.test/existing",
                NetworkId = existingNetworkId,
                IsFavorite = false,
                SyncedAtUtc = initialCursorAt.AddDays(-2),
            },
            new PrdbSite
            {
                Id = deletedSiteId,
                Title = "Deleted Site",
                Url = "https://example.test/deleted",
                NetworkId = existingNetworkId,
                IsFavorite = true,
                FavoritedAtUtc = initialCursorAt.AddDays(-1),
                SyncedAtUtc = initialCursorAt.AddDays(-2),
            });

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbFavoriteSiteSyncCursorUtc = initialCursorAt;
        settings.PrdbFavoriteSiteSyncCursorId = initialCursorId;
        await _db.SaveChangesAsync();

        HttpRequestMessage? siteChangesRequest = null;

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/sites", StringComparison.Ordinal))
            {
                return Json(new
                {
                    items = new object[]
                    {
                        new
                        {
                            id = existingSiteId,
                            title = "Existing Site",
                            url = "https://example.test/existing",
                            networkId = existingNetworkId,
                            networkTitle = "Existing Network"
                        },
                        new
                        {
                            id = deletedSiteId,
                            title = "Deleted Site",
                            url = "https://example.test/deleted",
                            networkId = existingNetworkId,
                            networkTitle = "Existing Network"
                        }
                    },
                    totalCount = 2,
                    page = 1,
                    pageSize = 100
                });
            }

            if (path.EndsWith("/favorite-sites/changes", StringComparison.Ordinal))
            {
                siteChangesRequest = request;
                return Json(new
                {
                    items = new object[]
                    {
                        new
                        {
                            eventType = "updated",
                            favoriteSite = new
                            {
                                id = existingSiteId,
                                title = "Existing Site",
                                url = "https://example.test/existing",
                                networkId = existingNetworkId,
                                networkTitle = "Existing Network",
                                isDeleted = false,
                                deletedAtUtc = (string?)null,
                                favoritedAtUtc = "2026-04-08T12:05:00Z",
                                updatedAtUtc = "2026-04-08T12:05:00Z"
                            }
                        },
                        new
                        {
                            eventType = "deleted",
                            favoriteSite = new
                            {
                                id = deletedSiteId,
                                title = "Deleted Site",
                                url = "https://example.test/deleted",
                                networkId = existingNetworkId,
                                networkTitle = "Existing Network",
                                isDeleted = true,
                                deletedAtUtc = "2026-04-08T12:06:00Z",
                                favoritedAtUtc = "2026-04-07T12:00:00Z",
                                updatedAtUtc = "2026-04-08T12:06:00Z"
                            }
                        },
                        new
                        {
                            eventType = "created",
                            favoriteSite = new
                            {
                                id = newSiteId,
                                title = "New Favorite Site",
                                url = "https://example.test/new",
                                networkId = newNetworkId,
                                networkTitle = "New Network",
                                isDeleted = false,
                                deletedAtUtc = (string?)null,
                                favoritedAtUtc = "2026-04-08T12:07:00Z",
                                updatedAtUtc = "2026-04-08T12:07:00Z"
                            }
                        }
                    },
                    pageSize = 1000,
                    hasMore = false,
                    nextCursor = new
                    {
                        updatedAtUtc = "2026-04-08T12:07:00Z",
                        id = newSiteId
                    }
                });
            }

            if (path.EndsWith("/favorite-actors/changes", StringComparison.Ordinal))
            {
                return Json(new
                {
                    items = Array.Empty<object>(),
                    pageSize = 1000,
                    hasMore = false,
                    nextCursor = (object?)null
                });
            }

            if (path.EndsWith("/videos", StringComparison.Ordinal))
            {
                return Json(new
                {
                    items = Array.Empty<object>(),
                    totalCount = 0,
                    page = 1,
                    pageSize = 100
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = new PrdbSyncService(_db, new StubHttpClientFactory(httpClient), NullLogger<PrdbSyncService>.Instance, CreateNoOpUserImageSync());

        var result = await service.SyncAsync(CancellationToken.None);

        result.FavoriteSitesSynced.Should().Be(3);
        siteChangesRequest.Should().NotBeNull();
        siteChangesRequest!.RequestUri!.Query.Should().Contain($"Since={Uri.EscapeDataString(initialCursorAt.ToString("O"))}");
        siteChangesRequest.RequestUri.Query.Should().Contain($"SinceId={initialCursorId}");

        var existing = await _db.PrdbSites.SingleAsync(s => s.Id == existingSiteId);
        existing.IsFavorite.Should().BeTrue();
        existing.FavoritedAtUtc.Should().Be(new DateTime(2026, 4, 8, 12, 5, 0, DateTimeKind.Utc));

        var deleted = await _db.PrdbSites.SingleAsync(s => s.Id == deletedSiteId);
        deleted.IsFavorite.Should().BeFalse();
        deleted.FavoritedAtUtc.Should().BeNull();

        var created = await _db.PrdbSites.SingleAsync(s => s.Id == newSiteId);
        created.IsFavorite.Should().BeTrue();
        created.NetworkId.Should().Be(newNetworkId);

        var network = await _db.PrdbNetworks.SingleAsync(n => n.Id == newNetworkId);
        network.Title.Should().Be("New Network");

        settings = await _db.AppSettings.SingleAsync();
        settings.PrdbFavoriteSiteSyncCursorUtc.Should().Be(new DateTime(2026, 4, 8, 12, 7, 0, DateTimeKind.Utc));
        settings.PrdbFavoriteSiteSyncCursorId.Should().Be(newSiteId);
    }

    [Fact]
    public async Task SyncAsync_FavoriteSiteChanges_SendsUtcSinceWhenStoredCursorKindIsUnspecified()
    {
        var cursorAt = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Unspecified);
        var cursorId = Guid.NewGuid();

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbFavoriteSiteSyncCursorUtc = cursorAt;
        settings.PrdbFavoriteSiteSyncCursorId = cursorId;
        await _db.SaveChangesAsync();

        HttpRequestMessage? siteChangesRequest = null;

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/sites", StringComparison.Ordinal))
            {
                return Json(new
                {
                    items = Array.Empty<object>(),
                    totalCount = 0,
                    page = 1,
                    pageSize = 100
                });
            }

            if (path.EndsWith("/favorite-sites/changes", StringComparison.Ordinal))
            {
                siteChangesRequest = request;
                return Json(new
                {
                    items = Array.Empty<object>(),
                    pageSize = 1000,
                    hasMore = false,
                    nextCursor = (object?)null
                });
            }

            if (path.EndsWith("/favorite-actors/changes", StringComparison.Ordinal))
            {
                return Json(new
                {
                    items = Array.Empty<object>(),
                    pageSize = 1000,
                    hasMore = false,
                    nextCursor = (object?)null
                });
            }

            if (path.EndsWith("/videos", StringComparison.Ordinal))
            {
                return Json(new
                {
                    items = Array.Empty<object>(),
                    totalCount = 0,
                    page = 1,
                    pageSize = 100
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = new PrdbSyncService(_db, new StubHttpClientFactory(httpClient), NullLogger<PrdbSyncService>.Instance, CreateNoOpUserImageSync());

        await service.SyncAsync(CancellationToken.None);

        siteChangesRequest.Should().NotBeNull();
        siteChangesRequest!.RequestUri!.Query.Should().Contain("Since=2026-04-08T12%3A00%3A00.0000000Z");
        siteChangesRequest.RequestUri.Query.Should().Contain($"SinceId={cursorId}");
    }

    [Fact]
    public async Task SyncAsync_FavoriteActorChanges_AppliesCreateDeleteAndSeedsInitialCursor()
    {
        var existingActorId = Guid.NewGuid();
        var deletedActorId = Guid.NewGuid();
        var newActorId = Guid.NewGuid();

        _db.PrdbActors.AddRange(
            new PrdbActor
            {
                Id = existingActorId,
                Name = "Existing Actor",
                Gender = 0,
                IsFavorite = false,
                PrdbCreatedAtUtc = DateTime.UtcNow.AddDays(-10),
                PrdbUpdatedAtUtc = DateTime.UtcNow.AddDays(-10),
                SyncedAtUtc = DateTime.UtcNow.AddDays(-10),
            },
            new PrdbActor
            {
                Id = deletedActorId,
                Name = "Deleted Actor",
                Gender = 0,
                IsFavorite = true,
                FavoritedAtUtc = new DateTime(2026, 4, 7, 12, 0, 0, DateTimeKind.Utc),
                PrdbCreatedAtUtc = DateTime.UtcNow.AddDays(-10),
                PrdbUpdatedAtUtc = DateTime.UtcNow.AddDays(-10),
                SyncedAtUtc = DateTime.UtcNow.AddDays(-10),
            });

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrdbFavoriteSiteSyncCursorUtc = DateTime.UtcNow;
        settings.PrdbFavoriteSiteSyncCursorId = Guid.NewGuid();
        settings.PrdbFavoriteActorSyncCursorUtc = null;
        settings.PrdbFavoriteActorSyncCursorId = null;
        await _db.SaveChangesAsync();

        HttpRequestMessage? actorChangesRequest = null;

        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/sites", StringComparison.Ordinal))
            {
                return Json(new
                {
                    items = Array.Empty<object>(),
                    totalCount = 0,
                    page = 1,
                    pageSize = 100
                });
            }

            if (path.EndsWith("/favorite-sites/changes", StringComparison.Ordinal))
            {
                return Json(new
                {
                    items = Array.Empty<object>(),
                    pageSize = 1000,
                    hasMore = false,
                    nextCursor = (object?)null
                });
            }

            if (path.EndsWith("/favorite-actors/changes", StringComparison.Ordinal))
            {
                actorChangesRequest = request;
                return Json(new
                {
                    items = new object[]
                    {
                        new
                        {
                            eventType = "updated",
                            favoriteActor = new
                            {
                                id = existingActorId,
                                name = "Existing Actor Renamed",
                                gender = "female",
                                nationality = "us",
                                ethnicity = "white",
                                profileImageCdnPath = (string?)null,
                                isDeleted = false,
                                deletedAtUtc = (string?)null,
                                favoritedAtUtc = "2026-04-08T12:11:00Z",
                                updatedAtUtc = "2026-04-08T12:11:00Z"
                            }
                        },
                        new
                        {
                            eventType = "deleted",
                            favoriteActor = new
                            {
                                id = deletedActorId,
                                name = "Deleted Actor",
                                gender = "male",
                                nationality = "us",
                                ethnicity = "white",
                                profileImageCdnPath = (string?)null,
                                isDeleted = true,
                                deletedAtUtc = "2026-04-08T12:12:00Z",
                                favoritedAtUtc = "2026-04-07T12:00:00Z",
                                updatedAtUtc = "2026-04-08T12:12:00Z"
                            }
                        },
                        new
                        {
                            eventType = "created",
                            favoriteActor = new
                            {
                                id = newActorId,
                                name = "New Favorite Actor",
                                gender = "female",
                                nationality = "us",
                                ethnicity = "latina",
                                profileImageCdnPath = "/img/actor.jpg",
                                isDeleted = false,
                                deletedAtUtc = (string?)null,
                                favoritedAtUtc = "2026-04-08T12:13:00Z",
                                updatedAtUtc = "2026-04-08T12:13:00Z"
                            }
                        }
                    },
                    pageSize = 1000,
                    hasMore = false,
                    nextCursor = new
                    {
                        updatedAtUtc = "2026-04-08T12:13:00Z",
                        id = newActorId
                    }
                });
            }

            if (path.EndsWith("/videos", StringComparison.Ordinal))
            {
                return Json(new
                {
                    items = Array.Empty<object>(),
                    totalCount = 0,
                    page = 1,
                    pageSize = 100
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = new PrdbSyncService(_db, new StubHttpClientFactory(httpClient), NullLogger<PrdbSyncService>.Instance, CreateNoOpUserImageSync());

        var result = await service.SyncAsync(CancellationToken.None);

        result.FavoriteActorsSynced.Should().Be(3);
        actorChangesRequest.Should().NotBeNull();
        actorChangesRequest!.RequestUri!.Query.Should().Contain("PageSize=1000");
        actorChangesRequest.RequestUri.Query.Should().Contain($"Since={Uri.EscapeDataString(DateTime.UnixEpoch.ToString("O"))}");
        actorChangesRequest.RequestUri.Query.Should().NotContain("SinceId=");

        var existing = await _db.PrdbActors.SingleAsync(a => a.Id == existingActorId);
        existing.IsFavorite.Should().BeTrue();
        existing.Name.Should().Be("Existing Actor Renamed");

        var deleted = await _db.PrdbActors.SingleAsync(a => a.Id == deletedActorId);
        deleted.IsFavorite.Should().BeFalse();
        deleted.FavoritedAtUtc.Should().BeNull();

        var created = await _db.PrdbActors.SingleAsync(a => a.Id == newActorId);
        created.IsFavorite.Should().BeTrue();
        created.Name.Should().Be("New Favorite Actor");

        settings = await _db.AppSettings.SingleAsync();
        settings.PrdbFavoriteActorSyncCursorUtc.Should().Be(new DateTime(2026, 4, 8, 12, 13, 0, DateTimeKind.Utc));
        settings.PrdbFavoriteActorSyncCursorId.Should().Be(newActorId);
    }

    private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };

    private PrdbVideoUserImageSyncService CreateNoOpUserImageSync()
    {
        var emptyHandler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"items":[],"pageSize":1000,"hasMore":false,"nextCursor":null}""",
                    Encoding.UTF8, "application/json"),
            });
        var emptyFactory = new StubHttpClientFactory(
            new HttpClient(emptyHandler) { BaseAddress = new Uri("https://api.prdb.test/") });
        return new PrdbVideoUserImageSyncService(_db, emptyFactory, NullLogger<PrdbVideoUserImageSyncService>.Instance);
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
