using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.DownloadLogs;

public sealed class DownloadLogsListTests : IAsyncLifetime
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

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPage()
    {
        var response = await _client.GetAsync("/api/download-logs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedBody>();
        body.Should().NotBeNull();
        body!.Items.Should().BeEmpty();
        body.Total.Should().Be(0);
    }

    [Fact]
    public async Task GetAll_WithLogs_ReturnsPaged()
    {
        await SeedLogsAsync(3, DownloadStatus.Completed);

        var response = await _client.GetAsync("/api/download-logs?page=1&pageSize=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedBody>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(2);
        body.Total.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_SecondPage_ReturnsRemainingItems()
    {
        await SeedLogsAsync(3, DownloadStatus.Completed);

        var response = await _client.GetAsync("/api/download-logs?page=2&pageSize=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedBody>();
        body!.Items.Should().HaveCount(1);
        body.Total.Should().Be(3);
    }

    // ── Filter: search ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_SearchFilter_ReturnsMatchingLogs()
    {
        await SeedLogAsync("Alpha.Release.1080p", DownloadStatus.Completed);
        await SeedLogAsync("Beta.Release.720p",   DownloadStatus.Completed);

        var response = await _client.GetAsync("/api/download-logs?search=Alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedBody>();
        body!.Total.Should().Be(1);
        body.Items.Should().ContainSingle(i => i.NzbName == "Alpha.Release.1080p");
    }

    // ── Filter: status ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_StatusFilter_ReturnsOnlyMatchingStatus()
    {
        await SeedLogAsync("Completed.Release", DownloadStatus.Completed);
        await SeedLogAsync("Failed.Release",    DownloadStatus.Failed);

        var response = await _client.GetAsync($"/api/download-logs?status={(int)DownloadStatus.Failed}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedBody>();
        body!.Total.Should().Be(1);
        body.Items.Should().ContainSingle(i => i.NzbName == "Failed.Release");
    }

    // ── Filter: activeOnly ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ActiveOnly_ExcludesCompletedAndFailed()
    {
        await SeedLogAsync("Queued.Release",    DownloadStatus.Queued);
        await SeedLogAsync("Completed.Release", DownloadStatus.Completed);
        await SeedLogAsync("Failed.Release",    DownloadStatus.Failed);

        var response = await _client.GetAsync("/api/download-logs?activeOnly=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedBody>();
        body!.Total.Should().Be(1);
        body.Items.Should().ContainSingle(i => i.NzbName == "Queued.Release");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task SeedLogsAsync(int count, DownloadStatus status)
        => Task.WhenAll(Enumerable.Range(0, count).Select(i => SeedLogAsync($"Release.{i}.1080p", status)));

    private async Task SeedLogAsync(string nzbName, DownloadStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var indexer = new Indexer
        {
            Id          = Guid.NewGuid(),
            Title       = "Test Indexer",
            Url         = "https://indexer.test",
            ApiKey      = "key",
            ApiPath     = "/api",
            ParsingType = ParsingType.Newznab,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        var row = new IndexerRow
        {
            Id        = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Title     = nzbName,
            NzbId     = Guid.NewGuid().ToString(),
            NzbUrl    = "https://indexer.test/nzb",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var downloadClient = new DownloadClient
        {
            Id         = Guid.NewGuid(),
            Title      = "SABnzbd Test",
            ClientType = ClientType.Sabnzbd,
            Host       = "127.0.0.1",
            Port       = 19999,
            ApiKey     = "key",
            IsEnabled  = true,
            CreatedAt  = DateTime.UtcNow,
            UpdatedAt  = DateTime.UtcNow,
        };

        var log = new DownloadLog
        {
            Id               = Guid.NewGuid(),
            IndexerRowId     = row.Id,
            DownloadClientId = downloadClient.Id,
            NzbName          = nzbName,
            NzbUrl           = "https://indexer.test/nzb",
            Status           = status,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        };

        db.Indexers.Add(indexer);
        db.IndexerRows.Add(row);
        db.DownloadClients.Add(downloadClient);
        db.DownloadLogs.Add(log);
        await db.SaveChangesAsync();
    }

    private sealed record PagedBody(List<LogItem> Items, int Total);
    private sealed record LogItem(string NzbName);
}
