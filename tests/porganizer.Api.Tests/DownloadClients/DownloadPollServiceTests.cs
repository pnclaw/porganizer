using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using porganizer.Api.Features.DownloadClients;
using porganizer.Api.Features.Library;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.DownloadClients;

/// <summary>
/// Tests for the MissedPollCount / Failed / Completed state machine in DownloadPollService,
/// with emphasis on the bug where a client being temporarily unreachable (e.g. during a
/// server restart) was incorrectly advancing MissedPollCount and eventually marking
/// completed downloads as Failed.
/// </summary>
public sealed class DownloadPollServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public DownloadPollServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SABnzbd — client unreachable
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PollAsync_WhenSabnzbdQueueThrows_DoesNotIncrementMissedPollCount()
    {
        var (client, log) = await SeedSabnzbdDownloadAsync("nzo-1", DownloadStatus.Downloading, missedPollCount: 0);

        var service = BuildService(sabnzbdHandler: _ => throw new HttpRequestException("connection refused"));

        await service.PollAsync(CancellationToken.None);

        var saved = await _db.DownloadLogs.SingleAsync(l => l.Id == log.Id);
        saved.MissedPollCount.Should().Be(0);
        saved.Status.Should().Be(DownloadStatus.Downloading);
    }

    [Fact]
    public async Task PollAsync_WhenSabnzbdQueueThrows_DoesNotMarkAsFailed_EvenWithExistingMissedPolls()
    {
        // MissedPollCount was 2 before the restart; one more unreachable poll must not push it to 3.
        var (_, log) = await SeedSabnzbdDownloadAsync("nzo-1", DownloadStatus.Downloading, missedPollCount: 2);

        var service = BuildService(sabnzbdHandler: _ => throw new HttpRequestException("connection refused"));

        await service.PollAsync(CancellationToken.None);

        var saved = await _db.DownloadLogs.SingleAsync(l => l.Id == log.Id);
        saved.MissedPollCount.Should().Be(2);
        saved.Status.Should().Be(DownloadStatus.Downloading);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SABnzbd — item completed while porganizer was offline
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PollAsync_WhenSabnzbdHistoryShowsCompleted_MarksAsCompleted()
    {
        // Simulate: porganizer was down, SABnzbd finished the download. On first poll after
        // restart, the item is absent from the queue but present in history as COMPLETED.
        var (_, log) = await SeedSabnzbdDownloadAsync("nzo-1", DownloadStatus.Downloading, missedPollCount: 0);

        var service = BuildService(sabnzbdHandler: request =>
        {
            var mode = ParseMode(request.RequestUri!);
            return mode switch
            {
                "queue"   => Ok(EmptySabnzbdQueue()),
                "history" => Ok(SabnzbdHistory("nzo-1", "COMPLETED", "/downloads/nzo-1", bytes: 104857600)),
                _         => throw new InvalidOperationException($"Unexpected mode: {mode}"),
            };
        });

        await service.PollAsync(CancellationToken.None);

        var saved = await _db.DownloadLogs.SingleAsync(l => l.Id == log.Id);
        saved.Status.Should().Be(DownloadStatus.Completed);
        saved.MissedPollCount.Should().Be(0);
        saved.CompletedAt.Should().NotBeNull();
        saved.StoragePath.Should().Be("/downloads/nzo-1");
    }

    [Fact]
    public async Task PollAsync_WhenSabnzbdHistoryShowsCompleted_ResetsMissedPollCount()
    {
        // MissedPollCount accumulated (e.g. item was briefly unavailable before), but the
        // history now confirms it completed successfully — count must reset.
        var (_, log) = await SeedSabnzbdDownloadAsync("nzo-1", DownloadStatus.Downloading, missedPollCount: 2);

        var service = BuildService(sabnzbdHandler: request =>
        {
            var mode = ParseMode(request.RequestUri!);
            return mode switch
            {
                "queue"   => Ok(EmptySabnzbdQueue()),
                "history" => Ok(SabnzbdHistory("nzo-1", "COMPLETED", "/downloads/nzo-1", bytes: 104857600)),
                _         => throw new InvalidOperationException($"Unexpected mode: {mode}"),
            };
        });

        await service.PollAsync(CancellationToken.None);

        var saved = await _db.DownloadLogs.SingleAsync(l => l.Id == log.Id);
        saved.Status.Should().Be(DownloadStatus.Completed);
        saved.MissedPollCount.Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SABnzbd — history poll fails for a specific item
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PollAsync_WhenSabnzbdHistoryThrowsForSpecificItem_DoesNotIncrementMissedPollCount()
    {
        // Queue is reachable (item not in queue), but the per-item history lookup throws.
        // Status is unknown — must not advance MissedPollCount.
        var (_, log) = await SeedSabnzbdDownloadAsync("nzo-1", DownloadStatus.Downloading, missedPollCount: 0);

        var service = BuildService(sabnzbdHandler: request =>
        {
            var mode = ParseMode(request.RequestUri!);
            if (mode == "queue")   return Ok(EmptySabnzbdQueue());
            if (mode == "history") throw new HttpRequestException("timeout");
            throw new InvalidOperationException($"Unexpected mode: {mode}");
        });

        await service.PollAsync(CancellationToken.None);

        var saved = await _db.DownloadLogs.SingleAsync(l => l.Id == log.Id);
        saved.MissedPollCount.Should().Be(0);
        saved.Status.Should().Be(DownloadStatus.Downloading);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SABnzbd — item genuinely deleted / absent
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PollAsync_WhenSabnzbdItemGenuinelyAbsent_IncrementsMissedPollCount()
    {
        var (_, log) = await SeedSabnzbdDownloadAsync("nzo-1", DownloadStatus.Downloading, missedPollCount: 0);

        var service = BuildService(sabnzbdHandler: request =>
        {
            var mode = ParseMode(request.RequestUri!);
            return mode switch
            {
                "queue"   => Ok(EmptySabnzbdQueue()),
                "history" => Ok(EmptySabnzbdHistory()),
                _         => throw new InvalidOperationException($"Unexpected mode: {mode}"),
            };
        });

        await service.PollAsync(CancellationToken.None);

        var saved = await _db.DownloadLogs.SingleAsync(l => l.Id == log.Id);
        saved.MissedPollCount.Should().Be(1);
        saved.Status.Should().Be(DownloadStatus.Downloading);
    }

    [Fact]
    public async Task PollAsync_WhenSabnzbdQueueIsLarge_FiltersQueueByClientItemIdBeforeMarkingMissing()
    {
        // SABnzbd queue responses can be paged. If porganizer only sees the first page,
        // deep queued items look absent and can be marked Failed after repeated polls.
        var (_, log) = await SeedSabnzbdDownloadAsync("nzo-deep", DownloadStatus.Queued, missedPollCount: 2);

        var service = BuildService(sabnzbdHandler: request =>
        {
            var mode = ParseMode(request.RequestUri!);
            if (mode == "queue")
            {
                ParseQueryValue(request.RequestUri!, "nzo_ids").Should().Be("nzo-deep");
                return Ok(SabnzbdQueue("nzo-deep", "Queued"));
            }
            if (mode == "history")
            {
                throw new InvalidOperationException("History should not be queried for a queue hit.");
            }
            throw new InvalidOperationException($"Unexpected mode: {mode}");
        });

        await service.PollAsync(CancellationToken.None);

        var saved = await _db.DownloadLogs.SingleAsync(l => l.Id == log.Id);
        saved.MissedPollCount.Should().Be(0);
        saved.Status.Should().Be(DownloadStatus.Queued);
        saved.CompletedAt.Should().BeNull();
        saved.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task PollAsync_WhenSabnzbdItemAbsentFor3Polls_MarksAsFailed()
    {
        // Simulate the third consecutive missed poll: MissedPollCount starts at 2.
        var (_, log) = await SeedSabnzbdDownloadAsync("nzo-1", DownloadStatus.Downloading, missedPollCount: 2);

        var service = BuildService(sabnzbdHandler: request =>
        {
            var mode = ParseMode(request.RequestUri!);
            return mode switch
            {
                "queue"   => Ok(EmptySabnzbdQueue()),
                "history" => Ok(EmptySabnzbdHistory()),
                _         => throw new InvalidOperationException($"Unexpected mode: {mode}"),
            };
        });

        await service.PollAsync(CancellationToken.None);

        var saved = await _db.DownloadLogs.SingleAsync(l => l.Id == log.Id);
        saved.MissedPollCount.Should().Be(3);
        saved.Status.Should().Be(DownloadStatus.Failed);
        saved.CompletedAt.Should().NotBeNull();
        saved.ErrorMessage.Should().Contain("3 polls");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private DownloadPollService BuildService(Func<HttpRequestMessage, HttpResponseMessage>? sabnzbdHandler = null)
    {
        var sabnzbdFactory = new StubHttpClientFactory(
            sabnzbdHandler ?? (_ => throw new HttpRequestException("not configured")));

        var nzbgetFactory = new StubHttpClientFactory(
            _ => throw new HttpRequestException("nzbget not configured"));

        var prdbFactory = new StubHttpClientFactory(
            _ => new HttpResponseMessage(HttpStatusCode.OK));

        var sabnzbdPoller    = new SabnzbdPoller(sabnzbdFactory, NullLogger<SabnzbdPoller>.Instance);
        var nzbgetPoller     = new NzbgetPoller(nzbgetFactory, NullLogger<NzbgetPoller>.Instance);
        var fileSyncService  = new DownloadLogFileSyncService(_db, NullLogger<DownloadLogFileSyncService>.Instance);
        var libraryQueue     = new LibraryIndexQueueService(_db, NullLogger<LibraryIndexQueueService>.Instance);
        var fileMoveService  = new DownloadFileMoveService(_db, libraryQueue, NullLogger<DownloadFileMoveService>.Instance);

        return new DownloadPollService(
            _db,
            sabnzbdPoller,
            nzbgetPoller,
            fileSyncService,
            fileMoveService,
            libraryQueue,
            prdbFactory,
            NullLogger<DownloadPollService>.Instance);
    }

    private async Task<(DownloadClient client, DownloadLog log)> SeedSabnzbdDownloadAsync(
        string clientItemId,
        DownloadStatus status,
        int missedPollCount)
    {
        var indexer = new Indexer
        {
            Id        = Guid.NewGuid(),
            Title     = "Test Indexer",
            Url       = "https://indexer.test",
            ApiKey    = "key",
            ApiPath   = "/api",
            ParsingType = ParsingType.Newznab,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var row = new IndexerRow
        {
            Id        = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Title     = "Test.Release.1080p",
            NzbId     = "nzb-test",
            NzbUrl    = "https://indexer.test/nzb",
            NzbSize   = 100_000_000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var client = new DownloadClient
        {
            Id         = Guid.NewGuid(),
            Title      = "SABnzbd Test",
            ClientType = ClientType.Sabnzbd,
            Host       = "sabnzbd.test",
            Port       = 8080,
            ApiKey     = "sabnzbd-key",
            IsEnabled  = true,
            CreatedAt  = DateTime.UtcNow,
            UpdatedAt  = DateTime.UtcNow,
        };

        var log = new DownloadLog
        {
            Id               = Guid.NewGuid(),
            IndexerRowId     = row.Id,
            DownloadClientId = client.Id,
            NzbName          = "Test.Release.1080p",
            NzbUrl           = "https://indexer.test/nzb",
            ClientItemId     = clientItemId,
            Status           = status,
            MissedPollCount  = missedPollCount,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        };

        _db.Indexers.Add(indexer);
        _db.IndexerRows.Add(row);
        _db.DownloadClients.Add(client);
        _db.DownloadLogs.Add(log);
        await _db.SaveChangesAsync();

        return (client, log);
    }

    private static string? ParseMode(Uri uri) =>
        System.Web.HttpUtility.ParseQueryString(uri.Query)["mode"];

    private static string? ParseQueryValue(Uri uri, string name) =>
        System.Web.HttpUtility.ParseQueryString(uri.Query)[name];

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static string EmptySabnzbdQueue() =>
        JsonSerializer.Serialize(new { queue = new { slots = Array.Empty<object>() } });

    private static string SabnzbdQueue(string nzoId, string status) =>
        JsonSerializer.Serialize(new
        {
            queue = new
            {
                slots = new[]
                {
                    new
                    {
                        nzo_id = nzoId,
                        status,
                        mb = "100.00",
                        mbleft = "100.00",
                    }
                }
            }
        });

    private static string EmptySabnzbdHistory() =>
        JsonSerializer.Serialize(new { history = new { slots = Array.Empty<object>() } });

    private static string SabnzbdHistory(string nzoId, string status, string storage, long bytes) =>
        JsonSerializer.Serialize(new
        {
            history = new
            {
                slots = new[]
                {
                    new
                    {
                        nzo_id  = nzoId,
                        status,
                        bytes,
                        storage,
                        fail_message = string.Empty,
                    }
                }
            }
        });

    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(responder)) { Timeout = TimeSpan.FromSeconds(5) };

        private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(responder(request));
        }
    }
}
