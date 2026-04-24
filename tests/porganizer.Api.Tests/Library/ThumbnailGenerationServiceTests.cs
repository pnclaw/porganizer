using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using porganizer.Api.Common.Prdb;
using porganizer.Api.Features.Library;
using porganizer.Database;

namespace porganizer.Api.Tests.Library;

/// <summary>
/// Unit tests for ThumbnailGenerationService focusing on configuration-driven behavior
/// that does not require a real ffmpeg binary.
/// </summary>
public sealed class ThumbnailGenerationServiceTests : IAsyncDisposable
{
    private readonly AppDbContext _db;
    private readonly ThumbnailOptions _thumbnailOptions = new() { CachePath = Path.GetTempPath() };

    public ThumbnailGenerationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.GetTempPath()}/porganizer-unit-{Guid.NewGuid()}.db")
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated(); // seed data (AppSettings Id=1) is applied by HasData
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private ThumbnailGenerationService CreateService(IPrdbUserImageCheckService? prdbCheck = null) =>
        new(_db, new OptionsWrapper<ThumbnailOptions>(_thumbnailOptions),
            prdbCheck ?? new StubPrdbUserImageCheckService(0),
            NullLogger<ThumbnailGenerationService>.Instance);

    [Fact]
    public async Task GenerateAsync_WhenThumbnailGenerationDisabled_ReturnsFalse()
    {
        // Default AppSettings has ThumbnailGenerationEnabled = false
        var service = CreateService();

        var result = await service.GenerateAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_WhenEnabledButFileNotInDb_ReturnsFalse()
    {
        var settings = await _db.AppSettings.FirstAsync();
        settings.ThumbnailGenerationEnabled = true;
        await _db.SaveChangesAsync();

        var service = CreateService();

        var result = await service.GenerateAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_WhenEnabledButVideoFileNotOnDisk_ReturnsFalse()
    {
        var settings = await _db.AppSettings.FirstAsync();
        settings.ThumbnailGenerationEnabled = true;
        await _db.SaveChangesAsync();

        var folderId = Guid.NewGuid();
        _db.LibraryFolders.Add(new LibraryFolder
        {
            Id = folderId,
            Path = "/nonexistent/folder",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        var fileId = Guid.NewGuid();
        _db.LibraryFiles.Add(new LibraryFile
        {
            Id = fileId,
            LibraryFolderId = folderId,
            RelativePath = "missing.mp4",
            FileSize = 0,
            LastSeenAtUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        var result = await service.GenerateAsync(fileId, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_WhenMatchedVideoHasExistingPrdbUserImages_ReturnsFalseAndStampsCompletion()
    {
        var settings = await _db.AppSettings.FirstAsync();
        settings.ThumbnailGenerationEnabled = true;
        settings.PrdbApiKey = "test-key";
        await _db.SaveChangesAsync();

        var videoId = Guid.NewGuid();
        _db.PrdbSites.Add(new PrdbSite { Id = Guid.NewGuid(), Title = "S", Url = "https://s.test", SyncedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        var siteId = (await _db.PrdbSites.FirstAsync()).Id;
        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id = videoId, SiteId = siteId, Title = "V",
            PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow,
        });

        var tempFile = Path.GetTempFileName();
        try
        {
            var folderId = Guid.NewGuid();
            _db.LibraryFolders.Add(new LibraryFolder
            {
                Id = folderId, Path = Path.GetDirectoryName(tempFile)!,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            var fileId = Guid.NewGuid();
            _db.LibraryFiles.Add(new LibraryFile
            {
                Id = fileId,
                LibraryFolderId = folderId,
                RelativePath = Path.GetFileName(tempFile),
                FileSize = 0,
                VideoId = videoId,
                LastSeenAtUtc = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync();

            var service = CreateService(new StubPrdbUserImageCheckService(3));

            var result = await service.GenerateAsync(fileId, CancellationToken.None);

            result.Should().BeFalse();

            var file = await _db.LibraryFiles.SingleAsync(f => f.Id == fileId);
            file.VideoUserImageUploadCompletedAtUtc.Should().NotBeNull();
            file.VideoUserImageUploadCompletionReason.Should().Be(porganizer.Database.Enums.VideoUserImageUploadCompletionReason.SkippedPrdbAlreadyHasImages);
            file.VideoUserImageUploadRemoteImageCount.Should().Be(3);
            file.SpriteSheetGeneratedAtUtc.Should().BeNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GenerateAsync_WhenPrdbCheckFails_DoesNotStampCompletion()
    {
        var settings = await _db.AppSettings.FirstAsync();
        settings.ThumbnailGenerationEnabled = true;
        settings.PrdbApiKey = "test-key";
        await _db.SaveChangesAsync();

        var videoId = Guid.NewGuid();
        _db.PrdbSites.Add(new PrdbSite { Id = Guid.NewGuid(), Title = "S2", Url = "https://s2.test", SyncedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        var siteId = (await _db.PrdbSites.FirstAsync(s => s.Title == "S2")).Id;
        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id = videoId, SiteId = siteId, Title = "V2",
            PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow,
        });

        var tempFile = Path.GetTempFileName();
        try
        {
            var folderId = Guid.NewGuid();
            _db.LibraryFolders.Add(new LibraryFolder
            {
                Id = folderId, Path = Path.GetDirectoryName(tempFile)!,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            var fileId = Guid.NewGuid();
            _db.LibraryFiles.Add(new LibraryFile
            {
                Id = fileId,
                LibraryFolderId = folderId,
                RelativePath = Path.GetFileName(tempFile),
                FileSize = 0,
                VideoId = videoId,
                LastSeenAtUtc = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync();

            // null = API error → fail open, proceed to attempt ffmpeg (which will fail on empty file)
            var service = CreateService(new StubPrdbUserImageCheckService(null));

            var result = await service.GenerateAsync(fileId, CancellationToken.None);

            result.Should().BeFalse(); // ffmpeg will fail on the empty temp file

            var file = await _db.LibraryFiles.SingleAsync(f => f.Id == fileId);
            file.VideoUserImageUploadCompletedAtUtc.Should().BeNull("prdb check failure should not stamp completion");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubPrdbUserImageCheckService(int? result) : IPrdbUserImageCheckService
    {
        public Task<int?> GetUserImageCountAsync(Guid videoId, AppSettings settings, CancellationToken ct) =>
            Task.FromResult(result);

        public Task<int?> GetUserImageCountByOsHashAsync(string osHash, AppSettings settings, CancellationToken ct) =>
            Task.FromResult(result);
    }
}
