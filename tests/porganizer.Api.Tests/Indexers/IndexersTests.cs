using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Api.Features.Indexers;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.Indexers;

public sealed class IndexersTests : IAsyncLifetime
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

    // ── GET /api/indexers ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsOkWithArray()
    {
        var response = await _client.GetAsync("/api/indexers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<IndexerResponse[]>();
        body.Should().NotBeNull();
    }

    // ── POST /api/indexers ───────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithValidRequest_Returns201WithBody()
    {
        var response = await _client.PostAsJsonAsync("/api/indexers", new
        {
            title = "NZBGeek",
            url = "https://api.nzbgeek.info",
            parsingType = (int)ParsingType.Newznab,
            isEnabled = true,
            apiKey = "abc123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<IndexerResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.Title.Should().Be("NZBGeek");
        body.Url.Should().Be("https://api.nzbgeek.info");
        body.ParsingType.Should().Be((int)ParsingType.Newznab);
        body.IsEnabled.Should().BeTrue();
        body.BackfillDays.Should().Be(30);
    }

    [Fact]
    public async Task Create_WithEmptyTitle_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/indexers", new
        {
            title = "",
            url = "https://example.com",
            parsingType = (int)ParsingType.Newznab,
            isEnabled = true,
            apiKey = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithEmptyUrl_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/indexers", new
        {
            title = "Test",
            url = "",
            parsingType = (int)ParsingType.Newznab,
            isEnabled = true,
            apiKey = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/indexers/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task GetById_ExistingIndexer_Returns200WithCorrectData()
    {
        var created = await CreateIndexerAsync("GetById Test");

        var response = await _client.GetAsync($"/api/indexers/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<IndexerResponse>();
        body!.Id.Should().Be(created.Id);
        body.Title.Should().Be("GetById Test");
    }

    [Fact]
    public async Task GetById_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync($"/api/indexers/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PUT /api/indexers/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingIndexer_Returns200WithUpdatedData()
    {
        var created = await CreateIndexerAsync("Original Title");

        var response = await _client.PutAsJsonAsync($"/api/indexers/{created.Id}", new
        {
            title = "Updated Title",
            url = "https://updated.example.com",
            parsingType = (int)ParsingType.Newznab,
            isEnabled = false,
            apiKey = "new-key",
            backfillDays = 45,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<IndexerResponse>();
        body!.Title.Should().Be("Updated Title");
        body.Url.Should().Be("https://updated.example.com");
        body.IsEnabled.Should().BeFalse();
        body.ApiKey.Should().Be("new-key");
        body.BackfillDays.Should().Be(45);
    }

    [Fact]
    public async Task Update_IncreasingBackfillDays_ResetsOnlyThatIndexerBackfillState()
    {
        var created = await CreateIndexerAsync("Backfill Reset Test");
        var startedAt = DateTime.UtcNow.AddHours(-2);
        var cutoffUtc = DateTime.UtcNow.AddDays(-30);
        var completedAt = DateTime.UtcNow.AddHours(-1);
        var lastRunAt = DateTime.UtcNow.AddMinutes(-15);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var target = await db.Indexers.FirstAsync(i => i.Id == created.Id);
            target.BackfillDays = 30;
            target.BackfillStartedAtUtc = startedAt;
            target.BackfillCutoffUtc = cutoffUtc;
            target.BackfillCompletedAtUtc = completedAt;
            target.BackfillLastRunAtUtc = lastRunAt;
            target.BackfillCurrentOffset = 500;

            var other = new Indexer
            {
                Id = Guid.NewGuid(),
                Title = "Other Indexer",
                Url = "https://other.example.com",
                ParsingType = ParsingType.Newznab,
                IsEnabled = true,
                ApiKey = "other-key",
                ApiPath = "/api",
                BackfillDays = 20,
                BackfillStartedAtUtc = startedAt,
                BackfillCutoffUtc = startedAt.AddDays(-20),
                BackfillCompletedAtUtc = completedAt,
                BackfillLastRunAtUtc = lastRunAt,
                BackfillCurrentOffset = 300,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            db.Indexers.Add(other);
            await db.SaveChangesAsync();
        }

        var response = await _client.PutAsJsonAsync($"/api/indexers/{created.Id}", new
        {
            title = created.Title,
            url = created.Url,
            parsingType = created.ParsingType,
            isEnabled = created.IsEnabled,
            apiKey = created.ApiKey,
            apiPath = created.ApiPath,
            backfillDays = 60,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await verifyDb.Indexers.FirstAsync(i => i.Id == created.Id);
        updated.BackfillDays.Should().Be(60);
        updated.BackfillStartedAtUtc.Should().BeNull();
        updated.BackfillCutoffUtc.Should().BeNull();
        updated.BackfillCompletedAtUtc.Should().BeNull();
        updated.BackfillLastRunAtUtc.Should().BeNull();
        updated.BackfillCurrentOffset.Should().BeNull();

        var otherUpdated = await verifyDb.Indexers.FirstAsync(i => i.Title == "Other Indexer");
        otherUpdated.BackfillDays.Should().Be(20);
        otherUpdated.BackfillStartedAtUtc.Should().Be(startedAt);
        otherUpdated.BackfillCompletedAtUtc.Should().Be(completedAt);
        otherUpdated.BackfillCurrentOffset.Should().Be(300);
    }

    [Fact]
    public async Task Update_DecreasingBackfillDays_KeepsExistingBackfillState()
    {
        var created = await CreateIndexerAsync("Backfill Keep Test");
        var startedAt = DateTime.UtcNow.AddHours(-2);
        var cutoffUtc = DateTime.UtcNow.AddDays(-30);
        var completedAt = DateTime.UtcNow.AddHours(-1);
        var lastRunAt = DateTime.UtcNow.AddMinutes(-15);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var indexer = await db.Indexers.FirstAsync(i => i.Id == created.Id);
            indexer.BackfillDays = 30;
            indexer.BackfillStartedAtUtc = startedAt;
            indexer.BackfillCutoffUtc = cutoffUtc;
            indexer.BackfillCompletedAtUtc = completedAt;
            indexer.BackfillLastRunAtUtc = lastRunAt;
            indexer.BackfillCurrentOffset = 500;
            await db.SaveChangesAsync();
        }

        var response = await _client.PutAsJsonAsync($"/api/indexers/{created.Id}", new
        {
            title = created.Title,
            url = created.Url,
            parsingType = created.ParsingType,
            isEnabled = created.IsEnabled,
            apiKey = created.ApiKey,
            apiPath = created.ApiPath,
            backfillDays = 14,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await verifyDb.Indexers.FirstAsync(i => i.Id == created.Id);
        updated.BackfillDays.Should().Be(14);
        updated.BackfillStartedAtUtc.Should().Be(startedAt);
        updated.BackfillCutoffUtc.Should().Be(cutoffUtc);
        updated.BackfillCompletedAtUtc.Should().Be(completedAt);
        updated.BackfillLastRunAtUtc.Should().Be(lastRunAt);
        updated.BackfillCurrentOffset.Should().Be(500);
    }

    [Fact]
    public async Task Update_NonExistentId_Returns404()
    {
        var response = await _client.PutAsJsonAsync($"/api/indexers/{Guid.NewGuid()}", new
        {
            title = "Title",
            url = "https://example.com",
            parsingType = (int)ParsingType.Newznab,
            isEnabled = true,
            apiKey = "",
            backfillDays = 30,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /api/indexers/{id} ────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingIndexer_Returns204AndIsGone()
    {
        var created = await CreateIndexerAsync("To Delete");

        var deleteResponse = await _client.DeleteAsync($"/api/indexers/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/indexers/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_NonExistentId_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/indexers/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<IndexerResponse> CreateIndexerAsync(string title = "Test Indexer")
    {
        var response = await _client.PostAsJsonAsync("/api/indexers", new
        {
            title,
            url = "https://example.com/api",
            parsingType = (int)ParsingType.Newznab,
            isEnabled = true,
            apiKey = "test-key",
            backfillDays = 30,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IndexerResponse>())!;
    }
}
