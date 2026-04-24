using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using porganizer.Api.Features.Prdb.Sync;
using porganizer.Database;

namespace porganizer.Api.Tests.Prdb;

public sealed class PrdbLatestPreDbSyncServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SqliteConnection _connection;

    public PrdbLatestPreDbSyncServiceTests()
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
    public async Task RunAsync_BackfillStoresAllPreDbEntries_AndLinksVideosWithoutProjectionTable()
    {
        var siteId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        var linkedEntryId = Guid.NewGuid();
        var unlinkedEntryId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new
                {
                    id = linkedEntryId,
                    title = "Linked.Scene.Title",
                    createdAtUtc = "2026-04-04T10:00:00Z",
                    video = new
                    {
                        id = videoId,
                        title = "Linked Video",
                        releaseDate = "2026-04-01",
                        site = new
                        {
                            id = siteId,
                            title = "Test Site"
                        }
                    }
                },
                new
                {
                    id = unlinkedEntryId,
                    title = "Unlinked.Scene.Title",
                    createdAtUtc = "2026-04-04T11:00:00Z",
                    video = (object?)null
                }
            },
            totalCount = 2,
            page = 1,
            pageSize = 500
        });

        var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://api.prdb.test/")
        };

        var settings = _db.AppSettings.Single();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.PrenamesBackfillPage = 1;
        settings.PrenamesSyncCursorUtc = null;
        await _db.SaveChangesAsync();

        var service = new PrdbLatestPreDbSyncService(
            _db,
            new StubHttpClientFactory(httpClient),
            NullLogger<PrdbLatestPreDbSyncService>.Instance);

        await service.RunAsync(CancellationToken.None);

        _db.PrdbPreDbEntries.Should().HaveCount(2);
        _db.PrdbPreDbEntries.Should().Contain(e => e.Id == linkedEntryId && e.PrdbVideoId == videoId);
        _db.PrdbPreDbEntries.Should().Contain(e => e.Id == unlinkedEntryId && e.PrdbVideoId == null);

        _db.PrdbSites.Should().ContainSingle(s => s.Id == siteId);
        _db.PrdbVideos.Should().ContainSingle(v => v.Id == videoId);

        settings = _db.AppSettings.Single();
        settings.PrenamesBackfillPage.Should().BeNull();
        settings.PrenamesBackfillTotalCount.Should().Be(2);
        settings.PrenamesSyncCursorUtc.Should().NotBeNull();
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
