using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Api.Features.Settings;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.Settings;

public sealed class SettingsTests : IAsyncLifetime
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

    // ── GET /api/settings ────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsOkWithDefaults()
    {
        var response = await _client.GetAsync("/api/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        body.Should().NotBeNull();
        body!.PrdbApiKey.Should().BeEmpty();
        body.PrdbApiUrl.Should().Be("https://api.prdb.net");
        body.PreferredVideoQuality.Should().Be((int)VideoQuality.P2160);
    }

    // ── PUT /api/settings ────────────────────────────────────────────────────

    [Fact]
    public async Task Update_WithValidRequest_ReturnsOkWithUpdatedValues()
    {
        var response = await _client.PutAsJsonAsync("/api/settings", new
        {
            prdbApiKey = "my-secret-key",
            prdbApiUrl = "https://custom.prdb.net",
            preferredVideoQuality = (int)VideoQuality.P1080,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        body.Should().NotBeNull();
        body!.PrdbApiKey.Should().Be("my-secret-key");
        body.PrdbApiUrl.Should().Be("https://custom.prdb.net");
        body.PreferredVideoQuality.Should().Be((int)VideoQuality.P1080);
    }

    [Fact]
    public async Task Update_PersistsChanges()
    {
        await _client.PutAsJsonAsync("/api/settings", new
        {
            prdbApiKey = "persisted-key",
            prdbApiUrl = "https://api.prdb.net",
            preferredVideoQuality = (int)VideoQuality.P720,
        });

        var response = await _client.GetAsync("/api/settings");
        var body = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        body!.PrdbApiKey.Should().Be("persisted-key");
        body.PreferredVideoQuality.Should().Be((int)VideoQuality.P720);
    }

    // ── MinimumLogLevel ──────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsDefaultLogLevel()
    {
        var response = await _client.GetAsync("/api/settings");
        var body = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        body!.MinimumLogLevel.Should().Be("Information");
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Fatal")]
    public async Task Update_WithValidLogLevel_ReturnsOk(string level)
    {
        var response = await _client.PutAsJsonAsync("/api/settings", new
        {
            prdbApiKey = "",
            prdbApiUrl = "https://api.prdb.net",
            preferredVideoQuality = (int)VideoQuality.P2160,
            minimumLogLevel = level,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        body!.MinimumLogLevel.Should().Be(level);
    }

    [Fact]
    public async Task Update_WithInvalidLogLevel_ReturnsBadRequest()
    {
        var response = await _client.PutAsJsonAsync("/api/settings", new
        {
            prdbApiKey = "",
            prdbApiUrl = "https://api.prdb.net",
            preferredVideoQuality = (int)VideoQuality.P2160,
            minimumLogLevel = "Trace",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── AutoAddAllNewVideosFulfillAllQualities ───────────────────────────────

    [Fact]
    public async Task Get_ReturnsDefaultFulfillAllQualitiesAsFalse()
    {
        var response = await _client.GetAsync("/api/settings");
        var body = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        body!.AutoAddAllNewVideosFulfillAllQualities.Should().BeFalse();
    }

    [Fact]
    public async Task Update_FulfillAllQualitiesTrue_IsPersisted()
    {
        await _client.PutAsJsonAsync("/api/settings", new
        {
            prdbApiKey = "",
            prdbApiUrl = "https://api.prdb.net",
            preferredVideoQuality = (int)VideoQuality.P2160,
            minimumLogLevel = "Information",
            autoAddAllNewVideosFulfillAllQualities = true,
        });

        var response = await _client.GetAsync("/api/settings");
        var body = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        body!.AutoAddAllNewVideosFulfillAllQualities.Should().BeTrue();
    }

    // ── DownloadLibraryPath ──────────────────────────────────────────────────

    [Fact]
    public async Task Get_DownloadLibraryPath_IsNullByDefault()
    {
        var response = await _client.GetAsync("/api/settings");
        var body = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        body!.DownloadLibraryPath.Should().BeNull();
    }

    [Fact]
    public async Task Update_DownloadLibraryPath_IsPersisted()
    {
        var response = await _client.PutAsJsonAsync("/api/settings", new
        {
            prdbApiKey = "",
            prdbApiUrl = "https://api.prdb.net",
            preferredVideoQuality = (int)VideoQuality.P2160,
            minimumLogLevel = "Information",
            downloadLibraryPath = "/downloads/complete",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        body!.DownloadLibraryPath.Should().Be("/downloads/complete");
    }

    [Fact]
    public async Task Update_DownloadLibraryPath_CreatesLibraryFolder()
    {
        await _client.PutAsJsonAsync("/api/settings", new
        {
            prdbApiKey = "",
            prdbApiUrl = "https://api.prdb.net",
            preferredVideoQuality = (int)VideoQuality.P2160,
            minimumLogLevel = "Information",
            downloadLibraryPath = "/downloads/complete",
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var folder = await db.LibraryFolders
            .FirstOrDefaultAsync(f => f.Path == "/downloads/complete");

        folder.Should().NotBeNull();
        folder!.Label.Should().Be("Downloads");
    }

    [Fact]
    public async Task Update_DownloadLibraryPath_DoesNotCreateDuplicateFolder()
    {
        for (var i = 0; i < 2; i++)
        {
            await _client.PutAsJsonAsync("/api/settings", new
            {
                prdbApiKey = "",
                prdbApiUrl = "https://api.prdb.net",
                preferredVideoQuality = (int)VideoQuality.P2160,
                minimumLogLevel = "Information",
                downloadLibraryPath = "/downloads/complete",
            });
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.LibraryFolders
            .CountAsync(f => f.Path == "/downloads/complete");

        count.Should().Be(1);
    }

    [Fact]
    public async Task Update_DownloadLibraryPath_ClearedToNull_DoesNotCreateFolder()
    {
        var response = await _client.PutAsJsonAsync("/api/settings", new
        {
            prdbApiKey = "",
            prdbApiUrl = "https://api.prdb.net",
            preferredVideoQuality = (int)VideoQuality.P2160,
            minimumLogLevel = "Information",
            downloadLibraryPath = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.LibraryFolders.CountAsync();
        count.Should().Be(0);
    }
}
