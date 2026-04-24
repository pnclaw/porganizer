using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using porganizer.Api.Features.DownloadClients;
using porganizer.Api.Features.Prdb.Sync;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.Prdb;

public sealed class PrdbDownloadedFromIndexerSyncServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly string _tempRoot;

    public PrdbDownloadedFromIndexerSyncServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"porganizer-prdb-download-sync-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, true);
    }

    [Fact]
    public async Task RunAsync_CreatesParentRecord_ForCompletedLinkedDownloadWithFiles()
    {
        var recorded = new List<RecordedRequest>();
        var videoId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var remoteFileId = Guid.NewGuid();

        var log = await SeedCompletedLinkedLogAsync(
            "https://drunkenslug.example/api",
            videoId,
            "job-123",
            CreateDirectoryWithFiles(("video.mkv", 1024)));

        var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            recorded.Add(await RecordedRequest.FromAsync(request));

            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().Be("/downloaded-from-indexers");

            return Json(HttpStatusCode.Created, new
            {
                id = parentId,
                filenames = new[]
                {
                    new
                    {
                        id = remoteFileId,
                        filename = "video.mkv",
                    }
                }
            });
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = CreateService(httpClient);

        await service.RunAsync(CancellationToken.None);

        var saved = await _db.DownloadLogs.Include(l => l.Files).SingleAsync(l => l.Id == log.Id);
        saved.PrdbDownloadedFromIndexerSyncError.Should().BeNull();
        saved.Files.Should().ContainSingle();
        saved.PrdbDownloadedFromIndexerSyncAttemptedAtUtc.Should().NotBeNull();

        recorded.Should().HaveCount(1);
        recorded[0].Body.Should().Contain("\"videoId\"");
        recorded[0].Body.Should().Contain(videoId.ToString());
        recorded[0].Body.Should().Contain("\"indexerSource\":0");
        recorded[0].Body.Should().Contain("\"indexerId\":\"nzb-1\"");
        recorded[0].Body.Should().Contain("\"downloadIdentifier\":\"job-123\"");
        recorded[0].Body.Should().Contain("\"filename\":\"video.mkv\"");

        saved.PrdbDownloadedFromIndexerId.Should().Be(parentId);
        saved.PrdbDownloadedFromIndexerSyncedAtUtc.Should().NotBeNull();
        saved.PrdbDownloadedFromIndexerSyncError.Should().BeNull();
        saved.PrdbDownloadedFromIndexerSyncFingerprint.Should().NotBeNullOrWhiteSpace();
        saved.Files.Should().ContainSingle();
        saved.Files.Single().PrdbDownloadedFromIndexerFilenameId.Should().Be(remoteFileId);
    }

    [Fact]
    public async Task RunAsync_DoesNotSubmitUntilFilesExist()
    {
        var videoId = Guid.NewGuid();
        var missingPath = Path.Combine(_tempRoot, "missing-folder");

        var log = await SeedCompletedLinkedLogAsync(
            "https://drunkenslug.example/api",
            videoId,
            "job-123",
            missingPath);

        var recorded = new List<RecordedRequest>();
        var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            recorded.Add(await RecordedRequest.FromAsync(request));
            return Json(HttpStatusCode.Created, new { id = Guid.NewGuid(), filenames = Array.Empty<object>() });
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = CreateService(httpClient);

        await service.RunAsync(CancellationToken.None);

        recorded.Should().BeEmpty();

        var saved = await _db.DownloadLogs.SingleAsync(l => l.Id == log.Id);
        saved.PrdbDownloadedFromIndexerId.Should().BeNull();
        saved.PrdbDownloadedFromIndexerSyncAttemptedAtUtc.Should().BeNull();
        saved.PrdbDownloadedFromIndexerSyncedAtUtc.Should().BeNull();
        saved.PrdbDownloadedFromIndexerSyncError.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_ReconcilesExistingRemoteRecord_WithDeleteAddAndUpdate()
    {
        var videoId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var keptRemoteId = Guid.NewGuid();
        var deletedRemoteId = Guid.NewGuid();
        var newRemoteId = Guid.NewGuid();
        var storagePath = CreateDirectoryWithFiles(("keep.mkv", 2048), ("new.mkv", 512));

        var log = await SeedCompletedLinkedLogAsync(
            "https://nzbfinder.example/api",
            videoId,
            "job-456",
            storagePath,
            prdbDownloadedFromIndexerId: parentId,
            prdbFingerprint: "stale");

        var oldFile = new DownloadLogFile
        {
            Id = Guid.NewGuid(),
            DownloadLogId = log.Id,
            FileName = "old.mkv",
            FileSize = 999,
            PrdbDownloadedFromIndexerFilenameId = deletedRemoteId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var keepFile = new DownloadLogFile
        {
            Id = Guid.NewGuid(),
            DownloadLogId = log.Id,
            FileName = "keep.mkv",
            FileSize = 1000,
            PrdbDownloadedFromIndexerFilenameId = keptRemoteId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.DownloadLogFiles.AddRange(oldFile, keepFile);
        await _db.SaveChangesAsync();

        var recorded = new List<RecordedRequest>();
        var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            recorded.Add(await RecordedRequest.FromAsync(request));
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Delete && path == $"/downloaded-from-indexers/{parentId}/filenames/{deletedRemoteId}")
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            if (request.Method == HttpMethod.Put && path == $"/downloaded-from-indexers/{parentId}")
                return Json(HttpStatusCode.OK, new
                {
                    id = parentId,
                    filenames = new[]
                    {
                        new { id = keptRemoteId, filename = "keep.mkv" }
                    }
                });

            if (request.Method == HttpMethod.Post && path == $"/downloaded-from-indexers/{parentId}/filenames")
                return Json(HttpStatusCode.Created, new
                {
                    id = parentId,
                    filenames = new[]
                    {
                        new { id = keptRemoteId, filename = "keep.mkv" },
                        new { id = newRemoteId, filename = "new.mkv" }
                    }
                });

            if (request.Method == HttpMethod.Put && path == $"/downloaded-from-indexers/{parentId}/filenames/{keptRemoteId}")
                return Json(HttpStatusCode.OK, new
                {
                    id = parentId,
                    filenames = new[]
                    {
                        new { id = keptRemoteId, filename = "keep.mkv" },
                        new { id = newRemoteId, filename = "new.mkv" }
                    }
                });

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var service = CreateService(httpClient);

        await service.RunAsync(CancellationToken.None);

        var saved = await _db.DownloadLogs
            .Include(l => l.Files)
            .SingleAsync(l => l.Id == log.Id);
        saved.PrdbDownloadedFromIndexerSyncError.Should().BeNull();
        saved.PrdbDownloadedFromIndexerSyncAttemptedAtUtc.Should().NotBeNull();

        recorded.Select(r => $"{r.Method} {r.Path}").Should().Equal(
            $"DELETE /downloaded-from-indexers/{parentId}/filenames/{deletedRemoteId}",
            $"PUT /downloaded-from-indexers/{parentId}",
            $"POST /downloaded-from-indexers/{parentId}/filenames",
            $"PUT /downloaded-from-indexers/{parentId}/filenames/{keptRemoteId}");

        saved.PrdbDownloadedFromIndexerId.Should().Be(parentId);
        saved.PrdbDownloadedFromIndexerSyncError.Should().BeNull();
        saved.PrdbDownloadedFromIndexerSyncedAtUtc.Should().NotBeNull();
        saved.PrdbDownloadedFromIndexerSyncFingerprint.Should().NotBe("stale");
        saved.Files.Should().HaveCount(2);
        saved.Files.Should().Contain(f => f.FileName == "keep.mkv" && f.PrdbDownloadedFromIndexerFilenameId == keptRemoteId);
        saved.Files.Should().Contain(f => f.FileName == "new.mkv" && f.PrdbDownloadedFromIndexerFilenameId == newRemoteId);
        saved.Files.Should().NotContain(f => f.FileName == "old.mkv");
    }

    [Theory]
    [InlineData("https://indexer.example/get/nzb-1?apikey=secret123", "secret123", "https://indexer.example/get/nzb-1?apikey=[apikey]")]
    [InlineData("https://indexer.example/api?t=get&id=abc&apikey=secret123", "secret123", "https://indexer.example/api?t=get&id=abc&apikey=[apikey]")]
    [InlineData("https://indexer.example/api?apikey=secret&t=get", "secret", "https://indexer.example/api?apikey=[apikey]&t=get")]
    [InlineData("https://indexer.example/api?r=secret123&t=get&id=abc", "secret123", "https://indexer.example/api?r=[apikey]&t=get&id=abc")]
    [InlineData("https://indexer.example/get/nzb-1", "secret123", "https://indexer.example/get/nzb-1")]
    [InlineData("https://indexer.example/api?t=get&id=abc", "", "https://indexer.example/api?t=get&id=abc")]
    public void SanitizeNzbUrl_ReplacesApiKeyWithPlaceholder(string input, string indexerApiKey, string expected)
    {
        PrdbDownloadedFromIndexerSyncService.SanitizeNzbUrl(input, indexerApiKey).Should().Be(expected);
    }

    [Fact]
    public async Task RunAsync_SanitizesApiKeyInNzbUrl_BeforeSendingToPrdb()
    {
        var recorded = new List<RecordedRequest>();
        var videoId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var remoteFileId = Guid.NewGuid();

        var log = await SeedCompletedLinkedLogAsync(
            "https://drunkenslug.example/api",
            videoId,
            "job-123",
            CreateDirectoryWithFiles(("video.mkv", 1024)),
            nzbUrl: "https://drunkenslug.example/api?r=key&t=get&id=nzb-1");

        var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            recorded.Add(await RecordedRequest.FromAsync(request));
            return Json(HttpStatusCode.Created, new
            {
                id = parentId,
                filenames = new[] { new { id = remoteFileId, filename = "video.mkv" } }
            });
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        await CreateService(httpClient).RunAsync(CancellationToken.None);

        recorded.Should().HaveCount(1);
        recorded[0].Body.Should().Contain("r=[apikey]");
        recorded[0].Body.Should().NotContain("\"r=key\"");
    }

    [Fact]
    public async Task RunAsync_CreatesParentRecord_ForCompletedUnlinkedDownloadWithNullVideoId()
    {
        var recorded = new List<RecordedRequest>();
        var parentId = Guid.NewGuid();
        var remoteFileId = Guid.NewGuid();

        var log = await SeedCompletedUnlinkedLogAsync(
            "https://drunkenslug.example/api",
            "job-999",
            CreateDirectoryWithFiles(("video.mkv", 1024)));

        var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            recorded.Add(await RecordedRequest.FromAsync(request));

            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().Be("/downloaded-from-indexers");

            return Json(HttpStatusCode.Created, new
            {
                id = parentId,
                filenames = new[] { new { id = remoteFileId, filename = "video.mkv" } }
            });
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        await CreateService(httpClient).RunAsync(CancellationToken.None);

        recorded.Should().HaveCount(1);
        recorded[0].Body.Should().Contain("\"videoId\":null");
        recorded[0].Body.Should().Contain("\"filename\":\"video.mkv\"");

        var saved = await _db.DownloadLogs.Include(l => l.Files).SingleAsync(l => l.Id == log.Id);
        saved.PrdbDownloadedFromIndexerId.Should().Be(parentId);
        saved.PrdbDownloadedFromIndexerSyncError.Should().BeNull();
        saved.Files.Should().ContainSingle(f => f.PrdbDownloadedFromIndexerFilenameId == remoteFileId);
    }

    private PrdbDownloadedFromIndexerSyncService CreateService(HttpClient httpClient)
    {
        return new PrdbDownloadedFromIndexerSyncService(
            _db,
            new DownloadLogFileSyncService(_db, NullLogger<DownloadLogFileSyncService>.Instance),
            new StubHttpClientFactory(httpClient),
            NullLogger<PrdbDownloadedFromIndexerSyncService>.Instance);
    }

    private async Task<DownloadLog> SeedCompletedLinkedLogAsync(
        string indexerUrl,
        Guid videoId,
        string clientItemId,
        string storagePath,
        Guid? prdbDownloadedFromIndexerId = null,
        string? prdbFingerprint = null,
        string? nzbUrl = null)
    {
        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "Indexer",
            Url = indexerUrl,
            ApiKey = "key",
            ApiPath = "/api",
            ParsingType = ParsingType.Newznab,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var row = new IndexerRow
        {
            Id = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Title = "Release.Title.1080p",
            NzbId = "nzb-1",
            NzbUrl = "https://indexer.example/get/nzb-1",
            NzbSize = 123,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Title = "Client",
            ClientType = ClientType.Sabnzbd,
            Host = "localhost",
            Port = 8080,
            ApiKey = "api-key",
            Category = "movies",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var site = new PrdbSite
        {
            Id = Guid.NewGuid(),
            Title = "Site",
            Url = "https://site.example",
            NetworkId = null,
            SyncedAtUtc = DateTime.UtcNow,
        };

        var video = new PrdbVideo
        {
            Id = videoId,
            Title = "Video",
            SiteId = site.Id,
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        };

        var preDb = new PrdbPreDbEntry
        {
            Id = Guid.NewGuid(),
            Title = "Release.Title",
            CreatedAtUtc = DateTime.UtcNow,
            PrdbVideoId = videoId,
            PrdbSiteId = site.Id,
            SyncedAtUtc = DateTime.UtcNow,
        };

        var log = new DownloadLog
        {
            Id = Guid.NewGuid(),
            IndexerRowId = row.Id,
            DownloadClientId = client.Id,
            NzbName = row.Title,
            NzbUrl = nzbUrl ?? row.NzbUrl,
            ClientItemId = clientItemId,
            Status = DownloadStatus.Completed,
            StoragePath = storagePath,
            CompletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            PrdbDownloadedFromIndexerId = prdbDownloadedFromIndexerId,
            PrdbDownloadedFromIndexerSyncFingerprint = prdbFingerprint,
        };

        var match = new IndexerRowMatch
        {
            Id = Guid.NewGuid(),
            IndexerRowId = row.Id,
            PrdbVideoId = videoId,
            MatchedPreDbEntryId = preDb.Id,
            MatchedTitle = preDb.Title,
            MatchedAtUtc = DateTime.UtcNow,
        };

        _db.Indexers.Add(indexer);
        _db.IndexerRows.Add(row);
        _db.DownloadClients.Add(client);
        _db.PrdbSites.Add(site);
        _db.PrdbVideos.Add(video);
        _db.PrdbPreDbEntries.Add(preDb);
        _db.DownloadLogs.Add(log);
        _db.IndexerRowMatches.Add(match);
        await _db.SaveChangesAsync();

        return log;
    }

    private async Task<DownloadLog> SeedCompletedUnlinkedLogAsync(
        string indexerUrl,
        string clientItemId,
        string storagePath)
    {
        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "Indexer",
            Url = indexerUrl,
            ApiKey = "key",
            ApiPath = "/api",
            ParsingType = ParsingType.Newznab,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var row = new IndexerRow
        {
            Id = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Title = "Unmatched.Release.1080p",
            NzbId = "nzb-unmatched",
            NzbUrl = "https://indexer.example/get/nzb-unmatched",
            NzbSize = 123,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var client = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Title = "Client",
            ClientType = ClientType.Sabnzbd,
            Host = "localhost",
            Port = 8080,
            ApiKey = "api-key",
            Category = "movies",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var log = new DownloadLog
        {
            Id = Guid.NewGuid(),
            IndexerRowId = row.Id,
            DownloadClientId = client.Id,
            NzbName = row.Title,
            NzbUrl = row.NzbUrl,
            ClientItemId = clientItemId,
            Status = DownloadStatus.Completed,
            StoragePath = storagePath,
            CompletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.Indexers.Add(indexer);
        _db.IndexerRows.Add(row);
        _db.DownloadClients.Add(client);
        _db.DownloadLogs.Add(log);
        await _db.SaveChangesAsync();

        return log;
    }

    private string CreateDirectoryWithFiles(params (string FileName, int Bytes)[] files)
    {
        var path = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        foreach (var (fileName, bytes) in files)
        {
            var fullPath = Path.Combine(path, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, Enumerable.Repeat((byte)'A', bytes).ToArray());
        }

        return path;
    }


    private static HttpResponseMessage Json(HttpStatusCode statusCode, object payload)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }

    private sealed record RecordedRequest(string Method, string Path, string Body)
    {
        public static async Task<RecordedRequest> FromAsync(HttpRequestMessage request)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync();

            return new RecordedRequest(request.Method.Method, request.RequestUri!.AbsolutePath, body);
        }
    }
}
