using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Api.Features.DownloadLogs;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.DownloadClients;

public sealed class RecheckEndpointTests : IAsyncLifetime
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

    // ── POST /api/download-logs/{id}/recheck ────────────────────────────────

    [Fact]
    public async Task Recheck_WithFailedDownload_ResetsStatusAndReturnsLog()
    {
        var log = await SeedDownloadAsync(DownloadStatus.Failed, clientItemId: "nzo-abc");

        var response = await _client.PostAsync($"/api/download-logs/{log.Id}/recheck", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DownloadLogResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(log.Id);
        // After the recheck the log is no longer Failed (the client is unreachable in tests
        // so the poll skips advancing MissedPollCount — status ends up as Queued).
        body.Status.Should().NotBe((int)DownloadStatus.Failed);
        body.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Recheck_WithNonExistentId_Returns404()
    {
        var response = await _client.PostAsync($"/api/download-logs/{Guid.NewGuid()}/recheck", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recheck_WithNonFailedDownload_Returns400()
    {
        var log = await SeedDownloadAsync(DownloadStatus.Downloading, clientItemId: "nzo-abc");

        var response = await _client.PostAsync($"/api/download-logs/{log.Id}/recheck", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recheck_WithFailedDownloadMissingClientItemId_Returns400()
    {
        var log = await SeedDownloadAsync(DownloadStatus.Failed, clientItemId: null);

        var response = await _client.PostAsync($"/api/download-logs/{log.Id}/recheck", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<DownloadLog> SeedDownloadAsync(DownloadStatus status, string? clientItemId)
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
            Title     = "Test.Release.1080p",
            NzbId     = "nzb-test",
            NzbUrl    = "https://indexer.test/nzb",
            NzbSize   = 100_000_000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var downloadClient = new DownloadClient
        {
            Id         = Guid.NewGuid(),
            Title      = "SABnzbd Test",
            ClientType = ClientType.Sabnzbd,
            // 127.0.0.1 on an unused port gives an immediate connection-refused,
            // so the poll fails fast without waiting for DNS or a TCP timeout.
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
            NzbName          = "Test.Release.1080p",
            NzbUrl           = "https://indexer.test/nzb",
            ClientItemId     = clientItemId,
            Status           = status,
            MissedPollCount  = 3,
            ErrorMessage     = status == DownloadStatus.Failed ? "Item not found after 3 polls" : null,
            CompletedAt      = status == DownloadStatus.Failed ? DateTime.UtcNow : null,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        };

        db.Indexers.Add(indexer);
        db.IndexerRows.Add(row);
        db.DownloadClients.Add(downloadClient);
        db.DownloadLogs.Add(log);
        await db.SaveChangesAsync();

        return log;
    }
}
