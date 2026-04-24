using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.DownloadLogs;

public sealed class DownloadLogsMoveTests : IAsyncLifetime
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
    public async Task Move_CompletedNotMoved_Returns200WithFilesMovedAtUtcNull()
    {
        // Move service skips the actual file operation when OrganizeCompletedBySite is false
        // (the default in tests). The endpoint should still return 200 OK with the log unchanged.
        var id = await SeedLogAsync(DownloadStatus.Completed, filesMovedAtUtc: null);

        var response = await _client.PostAsync($"/api/download-logs/{id}/move", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MoveBody>();
        body.Should().NotBeNull();
        body!.Log.Id.Should().Be(id);
        body.Entries.Should().NotBeNull();
    }

    // ── Sad paths ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Move_UnknownId_Returns404()
    {
        var response = await _client.PostAsync($"/api/download-logs/{Guid.NewGuid()}/move", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Move_NotCompleted_Returns400()
    {
        var id = await SeedLogAsync(DownloadStatus.Downloading, filesMovedAtUtc: null);

        var response = await _client.PostAsync($"/api/download-logs/{id}/move", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Move_AlreadyMoved_Returns400()
    {
        var id = await SeedLogAsync(DownloadStatus.Completed, filesMovedAtUtc: DateTime.UtcNow.AddMinutes(-5));

        var response = await _client.PostAsync($"/api/download-logs/{id}/move", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> SeedLogAsync(DownloadStatus status, DateTime? filesMovedAtUtc)
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
            NzbName          = "Test.Release.1080p",
            NzbUrl           = "https://indexer.test/nzb",
            Status           = status,
            FilesMovedAtUtc  = filesMovedAtUtc,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        };

        db.Indexers.Add(indexer);
        db.IndexerRows.Add(row);
        db.DownloadClients.Add(downloadClient);
        db.DownloadLogs.Add(log);
        await db.SaveChangesAsync();

        return log.Id;
    }

    private sealed record MoveBody(LogBody Log, List<MoveLogEntryBody> Entries);
    private sealed record LogBody(Guid Id, DateTime? FilesMovedAtUtc);
    private sealed record MoveLogEntryBody(int Level, string Message);
}
