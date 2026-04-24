using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using porganizer.Api.Features.Library;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.Settings;

public sealed class ResetDatabaseTests : IAsyncLifetime
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

    // ── POST /api/settings/reset-prdb-data ───────────────────────────────────

    [Fact]
    public async Task Reset_WithNoData_Returns204()
    {
        var response = await _client.PostAsync("/api/settings/reset-prdb-data", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reset_DeletesAllOperationalData()
    {
        await SeedOperationalDataAsync();

        var response = await _client.PostAsync("/api/settings/reset-prdb-data", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.PrdbNetworks.CountAsync()).Should().Be(0);
        (await db.PrdbSites.CountAsync()).Should().Be(0);
        (await db.PrdbVideos.CountAsync()).Should().Be(0);
        (await db.PrdbActors.CountAsync()).Should().Be(0);
        (await db.PrdbWantedVideos.CountAsync()).Should().Be(0);
        (await db.PrdbPreDbEntries.CountAsync()).Should().Be(0);
        (await db.PrdbVideoFilehashes.CountAsync()).Should().Be(0);
        (await db.IndexerRows.CountAsync()).Should().Be(0);
        (await db.IndexerRowMatches.CountAsync()).Should().Be(0);
        (await db.IndexerApiRequests.CountAsync()).Should().Be(0);
        (await db.DownloadLogs.CountAsync()).Should().Be(0);
        (await db.DownloadLogFiles.CountAsync()).Should().Be(0);
        (await db.LibraryFiles.CountAsync()).Should().Be(0);
        (await db.VideoUserImageUploads.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reset_PreservesDownloadClientsIndexersAndSettings()
    {
        await SeedProtectedDataAsync();

        var response = await _client.PostAsync("/api/settings/reset-prdb-data", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.DownloadClients.CountAsync()).Should().Be(1);
        (await db.Indexers.CountAsync()).Should().Be(1);
        (await db.FolderMappings.CountAsync()).Should().Be(1);
        (await db.LibraryFolders.CountAsync()).Should().Be(1);

        var settings = await db.AppSettings.FirstAsync();
        settings.PrdbApiKey.Should().Be("test-api-key");
        settings.PrdbApiUrl.Should().Be("https://api.prdb.net");
    }

    [Fact]
    public async Task Reset_ClearsAllSyncCursors()
    {
        await SetSyncCursorsAsync();

        var response = await _client.PostAsync("/api/settings/reset-prdb-data", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.AppSettings.FirstAsync();

        settings.PrdbActorSyncPage.Should().Be(1);
        settings.PrdbActorLastSyncedAt.Should().BeNull();
        settings.PrdbActorTotalCount.Should().BeNull();
        settings.SyncWorkerLastRunAt.Should().BeNull();
        settings.PrdbWantedVideoLastSyncedAt.Should().BeNull();
        settings.PrdbWantedVideoSyncCursorUtc.Should().BeNull();
        settings.PrdbWantedVideoSyncCursorId.Should().BeNull();
        settings.PrdbFavoriteSiteSyncCursorUtc.Should().BeNull();
        settings.PrdbFavoriteSiteSyncCursorId.Should().BeNull();
        settings.PrdbFavoriteActorSyncCursorUtc.Should().BeNull();
        settings.PrdbFavoriteActorSyncCursorId.Should().BeNull();
        settings.IndexerRowMatchLastRunAt.Should().BeNull();
        settings.PrenamesBackfillPage.Should().Be(1);
        settings.PrenamesBackfillTotalCount.Should().BeNull();
        settings.PrenamesSyncCursorUtc.Should().BeNull();
        settings.PrdbFilehashBackfillPage.Should().Be(1);
        settings.PrdbFilehashBackfillTotalCount.Should().BeNull();
        settings.PrdbFilehashSyncCursorUtc.Should().BeNull();
        settings.PrdbFilehashSyncCursorId.Should().BeNull();
        settings.FavoritesWantedLastRunAt.Should().BeNull();
        settings.AutoAddAllNewVideosLastRunAt.Should().BeNull();
    }

    [Fact]
    public async Task Reset_ClearsIndexerBackfillState()
    {
        await SeedIndexerWithBackfillStateAsync();

        var response = await _client.PostAsync("/api/settings/reset-prdb-data", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var indexer = await db.Indexers.FirstAsync();

        indexer.BackfillStartedAtUtc.Should().BeNull();
        indexer.BackfillCutoffUtc.Should().BeNull();
        indexer.BackfillCompletedAtUtc.Should().BeNull();
        indexer.BackfillLastRunAtUtc.Should().BeNull();
        indexer.BackfillCurrentOffset.Should().BeNull();

        // Credentials and config must be untouched
        indexer.Title.Should().Be("Test Indexer");
        indexer.ApiKey.Should().Be("secret-key");
    }

    [Fact]
    public async Task Reset_DeletesThumbnailAndPreviewFilesFromDisk()
    {
        // Resolve the cache paths the application is using
        var thumbnailCachePath = _factory.Services.GetRequiredService<IOptions<ThumbnailOptions>>().Value.CachePath;
        var previewCachePath   = _factory.Services.GetRequiredService<IOptions<PreviewOptions>>().Value.CachePath;

        // Plant fake per-file subdirectories to simulate generated files
        var thumbFileId   = Guid.NewGuid().ToString();
        var previewFileId = Guid.NewGuid().ToString();
        var thumbDir   = Path.Combine(thumbnailCachePath, thumbFileId);
        var previewDir = Path.Combine(previewCachePath, previewFileId);

        Directory.CreateDirectory(thumbDir);
        await File.WriteAllTextAsync(Path.Combine(thumbDir, "sprite.jpg"), "fake");

        Directory.CreateDirectory(previewDir);
        await File.WriteAllTextAsync(Path.Combine(previewDir, "preview_1.jpg"), "fake");

        try
        {
            var response = await _client.PostAsync("/api/settings/reset-prdb-data", null);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            Directory.Exists(thumbDir).Should().BeFalse();
            Directory.Exists(previewDir).Should().BeFalse();
        }
        finally
        {
            // Best-effort cleanup in case the test fails
            if (Directory.Exists(thumbDir))   Directory.Delete(thumbDir,   recursive: true);
            if (Directory.Exists(previewDir)) Directory.Delete(previewDir, recursive: true);
        }
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────

    private async Task SeedOperationalDataAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var network = new PrdbNetwork { Id = Guid.NewGuid(), Title = "Net", SyncedAtUtc = DateTime.UtcNow };
        var site = new PrdbSite { Id = Guid.NewGuid(), Title = "Site", Url = "https://s.example", SyncedAtUtc = DateTime.UtcNow };
        var video = new PrdbVideo { Id = Guid.NewGuid(), Title = "Video", SiteId = site.Id, SyncedAtUtc = DateTime.UtcNow };
        var actor = new PrdbActor { Id = Guid.NewGuid(), Name = "Actor" };

        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "Indexer",
            Url = "https://i.example",
            ApiKey = "k",
            ApiPath = "/api",
            IsEnabled = true,
        };
        var row = new IndexerRow
        {
            Id = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Title = "Row",
            NzbId = "nzb1",
            NzbUrl = "https://nzb.example",
            NzbSize = 100,
            Category = 5000,
        };

        var downloadClient = new DownloadClient
        {
            Id = Guid.NewGuid(),
            Title = "SAB",
            ClientType = ClientType.Sabnzbd,
            Host = "localhost",
            Port = 8080,
            ApiKey = "k",
            IsEnabled = true,
        };
        var downloadLog = new DownloadLog
        {
            Id = Guid.NewGuid(),
            IndexerRowId = row.Id,
            DownloadClientId = downloadClient.Id,
            NzbName = "test",
            NzbUrl = "https://nzb.example",
            Status = DownloadStatus.Completed,
        };

        var libraryFolder = new LibraryFolder { Id = Guid.NewGuid(), Path = "/videos" };
        var libraryFile = new LibraryFile
        {
            Id = Guid.NewGuid(),
            LibraryFolderId = libraryFolder.Id,
            RelativePath = "test.mp4",
            FileSize = 1_000_000,
            LastSeenAtUtc = DateTime.UtcNow,
        };

        db.PrdbNetworks.Add(network);
        db.PrdbSites.Add(site);
        db.PrdbVideos.Add(video);
        db.PrdbActors.Add(actor);
        db.Indexers.Add(indexer);
        db.IndexerRows.Add(row);
        db.DownloadClients.Add(downloadClient);
        db.DownloadLogs.Add(downloadLog);
        db.LibraryFolders.Add(libraryFolder);
        db.LibraryFiles.Add(libraryFile);

        await db.SaveChangesAsync();
    }

    private async Task SeedProtectedDataAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.DownloadClients.Add(new DownloadClient
        {
            Id = Guid.NewGuid(),
            Title = "SAB",
            ClientType = ClientType.Sabnzbd,
            Host = "localhost",
            Port = 8080,
            ApiKey = "client-key",
            IsEnabled = true,
        });
        db.Indexers.Add(new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "NZBGeek",
            Url = "https://nzbgeek.info",
            ApiKey = "indexer-key",
            ApiPath = "/api",
            IsEnabled = true,
        });
        db.FolderMappings.Add(new FolderMapping
        {
            Id = Guid.NewGuid(),
            OriginalFolder = "/downloads",
            MappedToFolder = "/mnt/downloads",
        });
        db.LibraryFolders.Add(new LibraryFolder { Id = Guid.NewGuid(), Path = "/videos" });

        var settings = await db.AppSettings.FirstAsync();
        settings.PrdbApiKey = "test-api-key";

        await db.SaveChangesAsync();
    }

    private async Task SetSyncCursorsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var settings = await db.AppSettings.FirstAsync();
        var now = DateTime.UtcNow;
        settings.PrdbActorSyncPage              = 5;
        settings.PrdbActorLastSyncedAt          = now;
        settings.PrdbActorTotalCount            = 1000;
        settings.SyncWorkerLastRunAt            = now;
        settings.PrdbWantedVideoLastSyncedAt    = now;
        settings.PrdbWantedVideoSyncCursorUtc   = now;
        settings.PrdbWantedVideoSyncCursorId    = Guid.NewGuid();
        settings.PrdbFavoriteSiteSyncCursorUtc  = now;
        settings.PrdbFavoriteSiteSyncCursorId   = Guid.NewGuid();
        settings.PrdbFavoriteActorSyncCursorUtc = now;
        settings.PrdbFavoriteActorSyncCursorId  = Guid.NewGuid();
        settings.IndexerRowMatchLastRunAt       = now;
        settings.PrenamesBackfillPage           = 3;
        settings.PrenamesBackfillTotalCount     = 500;
        settings.PrenamesSyncCursorUtc          = now;
        settings.PrdbFilehashBackfillPage       = 2;
        settings.PrdbFilehashBackfillTotalCount = 200;
        settings.PrdbFilehashSyncCursorUtc      = now;
        settings.PrdbFilehashSyncCursorId       = Guid.NewGuid();
        settings.FavoritesWantedLastRunAt       = now;
        settings.AutoAddAllNewVideosLastRunAt   = now;

        await db.SaveChangesAsync();
    }

    private async Task SeedIndexerWithBackfillStateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Indexers.Add(new Indexer
        {
            Id = Guid.NewGuid(),
            Title = "Test Indexer",
            Url = "https://indexer.example",
            ApiKey = "secret-key",
            ApiPath = "/api",
            IsEnabled = true,
            BackfillStartedAtUtc = DateTime.UtcNow.AddHours(-1),
            BackfillCutoffUtc = DateTime.UtcNow.AddDays(-30),
            BackfillCompletedAtUtc = DateTime.UtcNow,
            BackfillLastRunAtUtc = DateTime.UtcNow,
            BackfillCurrentOffset = 200,
        });

        await db.SaveChangesAsync();
    }
}
