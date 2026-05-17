using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Api.Features.DownloadClients;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.DownloadClients;

public sealed class DownloadClientsSendTests : IAsyncLifetime
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

    [Fact]
    public async Task Send_WhenDownloadClientDisabled_Returns400AndDoesNotCreateTrackingRows()
    {
        var (downloadClientId, indexerId, indexerRowId) = await SeedDisabledDownloadClientAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/download-clients/{downloadClientId}/send",
            new SendNzbRequest
            {
                IndexerId = indexerId,
                IndexerRowId = indexerRowId,
                Name = "Test.Release.1080p",
                NzbUrl = "https://indexer.test/nzb",
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var message = await response.Content.ReadAsStringAsync();
        message.Should().Contain("disabled");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DownloadLogs.CountAsync()).Should().Be(0);
        (await db.IndexerApiRequests.CountAsync()).Should().Be(0);
    }

    private async Task<(Guid DownloadClientId, Guid IndexerId, Guid IndexerRowId)> SeedDisabledDownloadClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "Test Indexer",
            Url = "https://indexer.test",
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
            Title = "Test.Release.1080p",
            NzbId = "nzb-test",
            NzbUrl = "https://indexer.test/nzb",
            NzbSize = 100_000_000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var downloadClient = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Title = "Disabled SABnzbd",
            ClientType = ClientType.Sabnzbd,
            Host = "127.0.0.1",
            Port = 19999,
            ApiKey = "key",
            Category = "movies",
            IsEnabled = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.Indexers.Add(indexer);
        db.IndexerRows.Add(row);
        db.DownloadClients.Add(downloadClient);
        await db.SaveChangesAsync();

        return (downloadClient.Id, indexer.Id, row.Id);
    }
}
