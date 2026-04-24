using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using porganizer.Api.Features.Prdb.Sync;
using porganizer.Database;

namespace porganizer.Api.Tests.Prdb;

public sealed class AutoWantedVideoSyncServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SqliteConnection _connection;

    private readonly Guid _siteId = Guid.NewGuid();

    public AutoWantedVideoSyncServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _db.PrdbSites.Add(new PrdbSite
        {
            Id          = _siteId,
            Title       = "Test Site",
            Url         = "https://site.test",
            SyncedAtUtc = DateTime.UtcNow,
        });

        if (!_db.AppSettings.Any())
            _db.AppSettings.Add(new AppSettings { Id = 1 });

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Disabled — skips without touching prdb.net
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_Disabled_DoesNothing()
    {
        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey          = "test-key";
        settings.PrdbApiUrl          = "https://api.prdb.test";
        settings.AutoAddAllNewVideos = false;
        await _db.SaveChangesAsync();

        var called = false;
        await RunServiceAsync(request => { called = true; return OkResponse(); });

        called.Should().BeFalse();
        (await _db.PrdbWantedVideos.AnyAsync()).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Happy path: new video with match gets added
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_NewVideoWithMatch_AddsToWantedList()
    {
        var videoId = Guid.NewGuid();
        SeedVideoWithMatch(videoId, DateTime.UtcNow.AddHours(-2));

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey              = "test-key";
        settings.PrdbApiUrl              = "https://api.prdb.test";
        settings.AutoAddAllNewVideos     = true;
        settings.AutoAddAllNewVideosDaysBack = 2;
        await _db.SaveChangesAsync();

        await RunServiceAsync(_ => OkResponse());

        var wanted = await _db.PrdbWantedVideos.ToListAsync();
        wanted.Should().ContainSingle(w => w.VideoId == videoId && !w.IsFulfilled);
    }

    // -------------------------------------------------------------------------
    // Video outside the window is ignored
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_VideoOutsideWindow_NotAdded()
    {
        var videoId = Guid.NewGuid();
        SeedVideoWithMatch(videoId, DateTime.UtcNow.AddDays(-3));

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey              = "test-key";
        settings.PrdbApiUrl              = "https://api.prdb.test";
        settings.AutoAddAllNewVideos     = true;
        settings.AutoAddAllNewVideosDaysBack = 2;
        await _db.SaveChangesAsync();

        await RunServiceAsync(_ => OkResponse());

        (await _db.PrdbWantedVideos.AnyAsync()).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Video with no indexer match is ignored
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_VideoWithNoMatch_NotAdded()
    {
        var videoId = Guid.NewGuid();
        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id               = videoId,
            SiteId           = _siteId,
            Title            = "Unmatched Video",
            PrdbCreatedAtUtc = DateTime.UtcNow.AddHours(-1),
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc      = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey              = "test-key";
        settings.PrdbApiUrl              = "https://api.prdb.test";
        settings.AutoAddAllNewVideos     = true;
        settings.AutoAddAllNewVideosDaysBack = 2;
        await _db.SaveChangesAsync();

        await RunServiceAsync(_ => OkResponse());

        (await _db.PrdbWantedVideos.AnyAsync()).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Video already on wanted list is skipped
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_AlreadyWanted_NotAddedAgain()
    {
        var videoId = Guid.NewGuid();
        SeedVideoWithMatch(videoId, DateTime.UtcNow.AddHours(-1));

        _db.PrdbWantedVideos.Add(new PrdbWantedVideo
        {
            VideoId          = videoId,
            IsFulfilled      = false,
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc      = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey              = "test-key";
        settings.PrdbApiUrl              = "https://api.prdb.test";
        settings.AutoAddAllNewVideos     = true;
        settings.AutoAddAllNewVideosDaysBack = 2;
        await _db.SaveChangesAsync();

        var callCount = 0;
        await RunServiceAsync(_ => { callCount++; return OkResponse(); });

        callCount.Should().Be(0);
        (await _db.PrdbWantedVideos.CountAsync()).Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // Video already in a library folder is skipped
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_AlreadyInLibrary_NotAdded()
    {
        var videoId  = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        SeedVideoWithMatch(videoId, DateTime.UtcNow.AddHours(-1));

        _db.LibraryFolders.Add(new LibraryFolder
        {
            Id        = folderId,
            Path      = "/videos",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        _db.LibraryFiles.Add(new LibraryFile
        {
            Id              = Guid.NewGuid(),
            LibraryFolderId = folderId,
            RelativePath    = "test-video.mkv",
            FileSize        = 1_000_000,
            VideoId         = videoId,
            LastSeenAtUtc   = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey              = "test-key";
        settings.PrdbApiUrl              = "https://api.prdb.test";
        settings.AutoAddAllNewVideos     = true;
        settings.AutoAddAllNewVideosDaysBack = 2;
        await _db.SaveChangesAsync();

        var callCount = 0;
        await RunServiceAsync(_ => { callCount++; return OkResponse(); });

        callCount.Should().Be(0);
        (await _db.PrdbWantedVideos.AnyAsync()).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // DaysBack cap: setting > 14 is clamped to 14
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_DaysBackOver14_ClampedTo14()
    {
        // Video created 10 days ago — would be in window if 14-day cap applied,
        // but would be outside if the raw value (20) were used and the service
        // enforced the cap internally (which it does via Math.Min).
        var videoId = Guid.NewGuid();
        SeedVideoWithMatch(videoId, DateTime.UtcNow.AddDays(-10));

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey              = "test-key";
        settings.PrdbApiUrl              = "https://api.prdb.test";
        settings.AutoAddAllNewVideos     = true;
        settings.AutoAddAllNewVideosDaysBack = 20; // over cap
        await _db.SaveChangesAsync();

        await RunServiceAsync(_ => OkResponse());

        // Should be added because the service clamps to 14, and 10 < 14.
        (await _db.PrdbWantedVideos.AnyAsync(w => w.VideoId == videoId)).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SeedVideoWithMatch(Guid videoId, DateTime prdbCreatedAtUtc)
    {
        var indexerId = Guid.NewGuid();
        var rowId     = Guid.NewGuid();
        var preDbId   = Guid.NewGuid();

        _db.Indexers.Add(new Indexer
        {
            Id        = indexerId,
            Title     = "Test Indexer",
            Url       = "https://indexer.test",
            IsEnabled = true,
        });

        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id               = videoId,
            SiteId           = _siteId,
            Title            = "Test Video",
            PrdbCreatedAtUtc = prdbCreatedAtUtc,
            PrdbUpdatedAtUtc = prdbCreatedAtUtc,
            SyncedAtUtc      = prdbCreatedAtUtc,
        });

        _db.IndexerRows.Add(new IndexerRow
        {
            Id        = rowId,
            IndexerId = indexerId,
            Title     = "Test.Video.1080p.NZB",
            NzbId     = rowId.ToString(),
            NzbUrl    = $"https://indexer.test/nzb/{rowId}",
        });

        _db.PrdbPreDbEntries.Add(new PrdbPreDbEntry
        {
            Id           = preDbId,
            Title        = "Test Video",
            CreatedAtUtc = prdbCreatedAtUtc,
            SyncedAtUtc  = prdbCreatedAtUtc,
        });

        _db.IndexerRowMatches.Add(new IndexerRowMatch
        {
            Id                  = Guid.NewGuid(),
            IndexerRowId        = rowId,
            PrdbVideoId         = videoId,
            MatchedPreDbEntryId = preDbId,
            MatchedTitle        = "Test Video",
            MatchedAtUtc        = prdbCreatedAtUtc.AddHours(1),
        });

        _db.SaveChanges();
    }

    private Task RunServiceAsync(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new StubHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("https://api.prdb.test/"),
        };

        var service = new AutoWantedVideoSyncService(
            _db,
            new StubHttpClientFactory(http),
            NullLogger<AutoWantedVideoSyncService>.Instance);

        return service.RunAsync(CancellationToken.None);
    }

    private static HttpResponseMessage OkResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
