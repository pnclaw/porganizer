using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using porganizer.Api.Features.DownloadClients;
using porganizer.Api.Features.WantedFulfillment;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.WantedFulfillment;

public sealed class WantedVideoFulfillmentServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SqliteConnection _connection;

    // Shared IDs
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _indexerId = Guid.NewGuid();
    private readonly Guid _siteId   = Guid.NewGuid();

    public WantedVideoFulfillmentServiceTests()
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

        SeedSharedEntities();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Happy path: a match that has no prior log gets queued exactly once
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_UnfulfilledVideoWithMatch_QueuesDownload()
    {
        var videoId = Guid.NewGuid();
        var rowId   = Guid.NewGuid();

        SeedVideo(videoId);
        SeedWantedVideo(videoId, isFulfilled: false);
        SeedIndexerRow(rowId, "Movie.1080p.NZB");
        SeedMatch(Guid.NewGuid(), rowId, videoId);
        await _db.SaveChangesAsync();

        await RunServiceAsync(AlwaysSucceedSender());

        var logs = await _db.DownloadLogs.ToListAsync();
        logs.Should().ContainSingle(l => l.IndexerRowId == rowId && l.Status == DownloadStatus.Queued);
        var log = logs.Single();
        log.CreatedAt.Should().BeAfter(DateTime.MinValue);
        log.UpdatedAt.Should().BeAfter(DateTime.MinValue);
    }

    // -------------------------------------------------------------------------
    // Sad path: already-queued video must not produce a second download log
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_VideoWithOneRowAlreadyQueued_DoesNotQueueSecondRow()
    {
        var videoId = Guid.NewGuid();
        var row1Id  = Guid.NewGuid();
        var row2Id  = Guid.NewGuid();

        SeedVideo(videoId);
        SeedWantedVideo(videoId, isFulfilled: false);
        SeedIndexerRow(row1Id, "Movie.1080p.NZB");
        SeedIndexerRow(row2Id, "Movie.720p.NZB");
        SeedMatch(Guid.NewGuid(), row1Id, videoId);
        SeedMatch(Guid.NewGuid(), row2Id, videoId);

        // row1 is already actively queued
        _db.DownloadLogs.Add(new DownloadLog
        {
            Id               = Guid.NewGuid(),
            IndexerRowId     = row1Id,
            DownloadClientId = _clientId,
            NzbName          = "Movie.1080p.NZB",
            NzbUrl           = "https://indexer.test/nzb/1",
            Status           = DownloadStatus.Queued,
        });

        await _db.SaveChangesAsync();

        await RunServiceAsync(AlwaysSucceedSender());

        // No new log should have been created — row2 is excluded because row1
        // already covers this video.
        var newLogs = await _db.DownloadLogs
            .Where(l => l.IndexerRowId == row2Id)
            .ToListAsync();

        newLogs.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Failed log blocks automatic retry — must be retried manually via recheck
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_VideoWithOnlyFailedLog_DoesNotQueueAgain()
    {
        var videoId = Guid.NewGuid();
        var rowId   = Guid.NewGuid();

        SeedVideo(videoId);
        SeedWantedVideo(videoId, isFulfilled: false);
        SeedIndexerRow(rowId, "Movie.1080p.NZB");
        SeedMatch(Guid.NewGuid(), rowId, videoId);

        _db.DownloadLogs.Add(new DownloadLog
        {
            Id               = Guid.NewGuid(),
            IndexerRowId     = rowId,
            DownloadClientId = _clientId,
            NzbName          = "Movie.1080p.NZB",
            NzbUrl           = "https://indexer.test/nzb/1",
            Status           = DownloadStatus.Failed,
        });

        await _db.SaveChangesAsync();

        await RunServiceAsync(AlwaysSucceedSender());

        var newQueued = await _db.DownloadLogs
            .Where(l => l.IndexerRowId == rowId && l.Status == DownloadStatus.Queued)
            .ToListAsync();

        newQueued.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_VideoWithPendingSendMarker_DoesNotSendAgain()
    {
        var videoId = Guid.NewGuid();
        var rowId   = Guid.NewGuid();
        var sendAttempts = 0;

        SeedVideo(videoId);
        SeedWantedVideo(videoId, isFulfilled: false);
        SeedIndexerRow(rowId, "Movie.1080p.NZB");
        SeedMatch(Guid.NewGuid(), rowId, videoId);

        _db.DownloadLogs.Add(new DownloadLog
        {
            Id               = Guid.NewGuid(),
            IndexerRowId     = rowId,
            DownloadClientId = _clientId,
            NzbName          = "Movie.1080p.NZB",
            NzbUrl           = "https://indexer.test/nzb/1",
            ClientItemId     = null,
            Status           = DownloadStatus.Queued,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync();

        await RunServiceAsync(Sender(request =>
        {
            sendAttempts++;
            return SuccessResponse();
        }));

        sendAttempts.Should().Be(0);
        (await _db.DownloadLogs.CountAsync(l => l.IndexerRowId == rowId)).Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WhenSendFails_MarksPendingLogFailed()
    {
        var videoId = Guid.NewGuid();
        var rowId   = Guid.NewGuid();

        SeedVideo(videoId);
        SeedWantedVideo(videoId, isFulfilled: false);
        SeedIndexerRow(rowId, "Movie.1080p.NZB");
        SeedMatch(Guid.NewGuid(), rowId, videoId);
        await _db.SaveChangesAsync();

        await RunServiceAsync(Sender(_ =>
        {
            var response = JsonSerializer.Serialize(new
            {
                status = false,
            });

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json"),
            };
        }));

        var log = await _db.DownloadLogs.SingleAsync(l => l.IndexerRowId == rowId);
        log.Status.Should().Be(DownloadStatus.Failed);
        log.ErrorMessage.Should().Contain("rejected");
        log.CompletedAt.Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // Fulfilled video is skipped entirely
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_FulfilledVideo_IsNotQueued()
    {
        var videoId = Guid.NewGuid();
        var rowId   = Guid.NewGuid();

        SeedVideo(videoId);
        SeedWantedVideo(videoId, isFulfilled: true);
        SeedIndexerRow(rowId, "Movie.1080p.NZB");
        SeedMatch(Guid.NewGuid(), rowId, videoId);
        await _db.SaveChangesAsync();

        await RunServiceAsync(AlwaysSucceedSender());

        (await _db.DownloadLogs.AnyAsync()).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // All-qualities mode: each quality gets its own download
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_FulfillAllQualities_QueuesOneDownloadPerQuality()
    {
        var videoId  = Guid.NewGuid();
        var row720Id = Guid.NewGuid();
        var row1080Id = Guid.NewGuid();
        var row2160Id = Guid.NewGuid();

        SeedVideo(videoId);
        SeedWantedVideo(videoId, isFulfilled: false, fulfillAllQualities: true);
        SeedIndexerRow(row720Id,  "Movie.720p.NZB");
        SeedIndexerRow(row1080Id, "Movie.1080p.NZB");
        SeedIndexerRow(row2160Id, "Movie.2160p.NZB");
        SeedMatch(Guid.NewGuid(), row720Id,  videoId);
        SeedMatch(Guid.NewGuid(), row1080Id, videoId);
        SeedMatch(Guid.NewGuid(), row2160Id, videoId);
        await _db.SaveChangesAsync();

        await RunServiceAsync(AlwaysSucceedSender());

        var logs = await _db.DownloadLogs.ToListAsync();
        logs.Should().HaveCount(3);
        logs.Select(l => l.IndexerRowId).Should().BeEquivalentTo([row720Id, row1080Id, row2160Id]);
    }

    [Fact]
    public async Task RunAsync_FulfillAllQualities_SkipsQualitiesWithExistingLog()
    {
        var videoId   = Guid.NewGuid();
        var row1080Id = Guid.NewGuid();
        var row2160Id = Guid.NewGuid();

        SeedVideo(videoId);
        SeedWantedVideo(videoId, isFulfilled: false, fulfillAllQualities: true);
        SeedIndexerRow(row1080Id, "Movie.1080p.NZB");
        SeedIndexerRow(row2160Id, "Movie.2160p.NZB");
        SeedMatch(Guid.NewGuid(), row1080Id, videoId);
        SeedMatch(Guid.NewGuid(), row2160Id, videoId);

        // 1080p already queued
        _db.DownloadLogs.Add(new DownloadLog
        {
            Id               = Guid.NewGuid(),
            IndexerRowId     = row1080Id,
            DownloadClientId = _clientId,
            NzbName          = "Movie.1080p.NZB",
            NzbUrl           = "https://indexer.test/nzb/1",
            Status           = DownloadStatus.Queued,
        });

        await _db.SaveChangesAsync();

        await RunServiceAsync(AlwaysSucceedSender());

        var newLogs = await _db.DownloadLogs.Where(l => l.Status == DownloadStatus.Queued).ToListAsync();
        // Only 2160p should have been newly queued; 1080p already had a log
        newLogs.Should().ContainSingle(l => l.IndexerRowId == row2160Id);
        newLogs.Should().NotContain(l => l.IndexerRowId == row1080Id && l.Id != newLogs.First().Id);
    }

    [Fact]
    public async Task RunAsync_FulfillAllQualities_FulfilledVideoIsSkipped()
    {
        var videoId  = Guid.NewGuid();
        var row2160Id = Guid.NewGuid();

        SeedVideo(videoId);
        SeedWantedVideo(videoId, isFulfilled: true, fulfillAllQualities: true);
        SeedIndexerRow(row2160Id, "Movie.2160p.NZB");
        SeedMatch(Guid.NewGuid(), row2160Id, videoId);
        await _db.SaveChangesAsync();

        await RunServiceAsync(AlwaysSucceedSender());

        (await _db.DownloadLogs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_FulfillAllQualities_OnlyQueuesQualitiesWithMatches()
    {
        var videoId   = Guid.NewGuid();
        var row2160Id = Guid.NewGuid();

        SeedVideo(videoId);
        SeedWantedVideo(videoId, isFulfilled: false, fulfillAllQualities: true);
        // Only a 2160p row — no 720p or 1080p
        SeedIndexerRow(row2160Id, "Movie.2160p.NZB");
        SeedMatch(Guid.NewGuid(), row2160Id, videoId);
        await _db.SaveChangesAsync();

        await RunServiceAsync(AlwaysSucceedSender());

        var logs = await _db.DownloadLogs.ToListAsync();
        logs.Should().ContainSingle(l => l.IndexerRowId == row2160Id);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SeedSharedEntities()
    {
        _db.DownloadClients.Add(new DownloadClient
        {
            Id        = _clientId,
            Title     = "Test SABnzbd",
            ClientType = ClientType.Sabnzbd,
            Host      = "localhost",
            Port      = 8080,
            IsEnabled = true,
        });

        _db.Indexers.Add(new Indexer
        {
            Id        = _indexerId,
            Title     = "Test Indexer",
            Url       = "https://indexer.test",
            IsEnabled = true,
        });

        _db.PrdbSites.Add(new PrdbSite
        {
            Id          = _siteId,
            Title       = "Test Site",
            Url         = "https://site.test",
            SyncedAtUtc = DateTime.UtcNow,
        });

        _db.SaveChanges();
    }

    private void SeedVideo(Guid videoId)
    {
        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id               = videoId,
            SiteId           = _siteId,
            Title            = "Test Video",
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc      = DateTime.UtcNow,
        });
    }

    private void SeedWantedVideo(Guid videoId, bool isFulfilled, bool fulfillAllQualities = false)
    {
        _db.PrdbWantedVideos.Add(new PrdbWantedVideo
        {
            VideoId             = videoId,
            IsFulfilled         = isFulfilled,
            FulfillAllQualities = fulfillAllQualities,
            PrdbCreatedAtUtc    = DateTime.UtcNow,
            PrdbUpdatedAtUtc    = DateTime.UtcNow,
            SyncedAtUtc         = DateTime.UtcNow,
        });
    }

    private void SeedIndexerRow(Guid rowId, string title)
    {
        _db.IndexerRows.Add(new IndexerRow
        {
            Id        = rowId,
            IndexerId = _indexerId,
            Title     = title,
            NzbId     = rowId.ToString(),
            NzbUrl    = $"https://indexer.test/nzb/{rowId}",
        });
    }

    private void SeedMatch(Guid matchId, Guid rowId, Guid videoId)
    {
        var preDbEntryId = Guid.NewGuid();
        _db.PrdbPreDbEntries.Add(new PrdbPreDbEntry
        {
            Id          = preDbEntryId,
            Title       = "Test PreDb",
            CreatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        });

        _db.IndexerRowMatches.Add(new IndexerRowMatch
        {
            Id                   = matchId,
            IndexerRowId         = rowId,
            PrdbVideoId          = videoId,
            MatchedPreDbEntryId  = preDbEntryId,
            MatchedTitle         = "Test PreDb",
            MatchedAtUtc         = DateTime.UtcNow,
        });
    }

    private Task RunServiceAsync(DownloadClientSender sender)
    {
        var service = new WantedVideoFulfillmentService(
            _db,
            sender,
            NullLogger<WantedVideoFulfillmentService>.Instance);

        return service.RunAsync(CancellationToken.None);
    }

    private DownloadClientSender AlwaysSucceedSender()
        => Sender(_ => SuccessResponse());

    private static HttpResponseMessage SuccessResponse()
    {
        var sabnzbdResponse = JsonSerializer.Serialize(new
        {
            status = true,
            nzo_ids = new[] { Guid.NewGuid().ToString() },
        });

        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(sabnzbdResponse, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private DownloadClientSender Sender(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new StubHttpClientFactory(responder));

    // Creates a fresh HttpClient per call so that setting Timeout doesn't throw
    // after the first request (HttpClient.Timeout is immutable once used).
    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new StubHttpMessageHandler(responder));
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
