using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using porganizer.Api.Features.Library;
using porganizer.Api.Features.Library.Cleanup;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.Library;

public sealed class LibraryCleanupServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly string _tempRoot;
    private readonly string _previewDir;
    private readonly string _thumbnailDir;
    private readonly string _libraryDir;

    public LibraryCleanupServiceTests()
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

        _tempRoot    = Path.Combine(Path.GetTempPath(), $"porganizer-cleanup-test-{Guid.NewGuid()}");
        _previewDir  = Path.Combine(_tempRoot, "previews");
        _thumbnailDir = Path.Combine(_tempRoot, "thumbnails");
        _libraryDir  = Path.Combine(_tempRoot, "library");

        Directory.CreateDirectory(_previewDir);
        Directory.CreateDirectory(_thumbnailDir);
        Directory.CreateDirectory(_libraryDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private LibraryCleanupService CreateService() => new(
        _db,
        Options.Create(new PreviewOptions { CachePath = _previewDir }),
        Options.Create(new ThumbnailOptions { CachePath = _thumbnailDir }),
        NullLogger<LibraryCleanupService>.Instance);

    private async Task<Guid> SeedFullyUploadedFileAsync(
        string? videoFile = "video.mp4",
        bool createVideoOnDisk = true,
        bool createPreviewDir = true,
        bool createThumbnailDir = true)
    {
        // Each call gets its own folder subfolder to avoid the unique-path constraint
        var folderPath = Path.Combine(_libraryDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(folderPath);

        var siteId = Guid.NewGuid();
        _db.PrdbSites.Add(new PrdbSite
        {
            Id = siteId, Title = "Test Site", Url = "https://example.test", SyncedAtUtc = DateTime.UtcNow,
        });

        var videoId = Guid.NewGuid();
        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id = videoId, SiteId = siteId, Title = "Test Video",
            PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow,
        });

        var folderId = Guid.NewGuid();
        _db.LibraryFolders.Add(new LibraryFolder
        {
            Id = folderId, Path = folderPath, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });

        var fileId = Guid.NewGuid();
        _db.LibraryFiles.Add(new LibraryFile
        {
            Id = fileId,
            LibraryFolderId = folderId,
            RelativePath = videoFile ?? "video.mp4",
            FileSize = 1_000_000,
            OsHash = "abcdef1234567890",
            VideoId = videoId,
            LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow,
            PreviewImageCount = 5,
            SpriteSheetGeneratedAtUtc = DateTime.UtcNow,
            SpriteSheetTileCount = 100,
        });

        // Seed 5 Single + 1 SpriteSheet upload records
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            _db.VideoUserImageUploads.Add(new VideoUserImageUpload
            {
                Id = Guid.NewGuid(), LibraryFileId = fileId, PrdbVideoId = videoId,
                PrdbVideoUserImageId = Guid.NewGuid(), PreviewImageType = "Single", DisplayOrder = i,
                UploadedAtUtc = now, CreatedAt = now, UpdatedAt = now,
            });
        }
        _db.VideoUserImageUploads.Add(new VideoUserImageUpload
        {
            Id = Guid.NewGuid(), LibraryFileId = fileId, PrdbVideoId = videoId,
            PrdbVideoUserImageId = Guid.NewGuid(), PreviewImageType = "SpriteSheet", DisplayOrder = 0,
            UploadedAtUtc = now, CreatedAt = now, UpdatedAt = now,
        });

        await _db.SaveChangesAsync();

        if (createVideoOnDisk)
            File.WriteAllBytes(Path.Combine(folderPath, videoFile ?? "video.mp4"), [0x00, 0x01, 0x02]);

        if (createPreviewDir)
        {
            var dir = Path.Combine(_previewDir, fileId.ToString());
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "preview_1.jpg"), [0xFF, 0xD8]);
        }

        if (createThumbnailDir)
        {
            var dir = Path.Combine(_thumbnailDir, fileId.ToString());
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "sprite.jpg"), [0xFF, 0xD8]);
        }

        return fileId;
    }

    private async Task<(Guid fileId, string folderPath)> SeedPartiallyUploadedFileAsync()
    {
        // Only 4 Singles + 1 SpriteSheet — missing one single, so NOT fully uploaded
        var folderPath = Path.Combine(_libraryDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(folderPath);

        var siteId = Guid.NewGuid();
        _db.PrdbSites.Add(new PrdbSite
        {
            Id = siteId, Title = "Partial Site", Url = "https://partial.test", SyncedAtUtc = DateTime.UtcNow,
        });

        var videoId = Guid.NewGuid();
        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id = videoId, SiteId = siteId, Title = "Partial Video",
            PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow,
        });

        var folderId = Guid.NewGuid();
        _db.LibraryFolders.Add(new LibraryFolder
        {
            Id = folderId, Path = folderPath, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });

        var fileId = Guid.NewGuid();
        _db.LibraryFiles.Add(new LibraryFile
        {
            Id = fileId, LibraryFolderId = folderId, RelativePath = "partial.mp4",
            FileSize = 500_000, OsHash = "0000000000000000", VideoId = videoId,
            LastSeenAtUtc = DateTime.UtcNow, PreviewImagesGeneratedAtUtc = DateTime.UtcNow,
            PreviewImageCount = 5, SpriteSheetGeneratedAtUtc = DateTime.UtcNow, SpriteSheetTileCount = 100,
        });

        var now = DateTime.UtcNow;
        for (var i = 0; i < 4; i++) // only 4 singles — not fully uploaded
        {
            _db.VideoUserImageUploads.Add(new VideoUserImageUpload
            {
                Id = Guid.NewGuid(), LibraryFileId = fileId, PrdbVideoId = videoId,
                PrdbVideoUserImageId = Guid.NewGuid(), PreviewImageType = "Single", DisplayOrder = i,
                UploadedAtUtc = now, CreatedAt = now, UpdatedAt = now,
            });
        }
        _db.VideoUserImageUploads.Add(new VideoUserImageUpload
        {
            Id = Guid.NewGuid(), LibraryFileId = fileId, PrdbVideoId = videoId,
            PrdbVideoUserImageId = Guid.NewGuid(), PreviewImageType = "SpriteSheet", DisplayOrder = 0,
            UploadedAtUtc = now, CreatedAt = now, UpdatedAt = now,
        });

        await _db.SaveChangesAsync();

        File.WriteAllBytes(Path.Combine(folderPath, "partial.mp4"), [0x00]);
        return (fileId, folderPath);
    }

    private async Task<Guid> SeedSkippedFileAsync(
        bool createVideoOnDisk = true,
        bool createPreviewDir = true,
        bool createThumbnailDir = true)
    {
        var folderPath = Path.Combine(_libraryDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(folderPath);

        var siteId = Guid.NewGuid();
        _db.PrdbSites.Add(new PrdbSite
        {
            Id = siteId, Title = "Skipped Site", Url = "https://skipped.test", SyncedAtUtc = DateTime.UtcNow,
        });

        var videoId = Guid.NewGuid();
        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id = videoId, SiteId = siteId, Title = "Skipped Video",
            PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow,
        });

        var folderId = Guid.NewGuid();
        _db.LibraryFolders.Add(new LibraryFolder
        {
            Id = folderId, Path = folderPath, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });

        var fileId = Guid.NewGuid();
        _db.LibraryFiles.Add(new LibraryFile
        {
            Id = fileId,
            LibraryFolderId = folderId,
            RelativePath = "video.mp4",
            FileSize = 1_000_000,
            VideoId = videoId,
            LastSeenAtUtc = DateTime.UtcNow,
            VideoUserImageUploadCompletedAtUtc = DateTime.UtcNow,
            VideoUserImageUploadCompletionReason = VideoUserImageUploadCompletionReason.SkippedPrdbAlreadyHasImages,
        });

        await _db.SaveChangesAsync();

        if (createVideoOnDisk)
            File.WriteAllBytes(Path.Combine(folderPath, "video.mp4"), [0x00, 0x01, 0x02]);

        if (createPreviewDir)
        {
            var dir = Path.Combine(_previewDir, fileId.ToString());
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "preview_1.jpg"), [0xFF, 0xD8]);
        }

        if (createThumbnailDir)
        {
            var dir = Path.Combine(_thumbnailDir, fileId.ToString());
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "sprite.jpg"), [0xFF, 0xD8]);
        }

        return fileId;
    }

    // ── Preview tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEligibleFiles_WhenFullyUploaded_ReturnsFile()
    {
        var fileId = await SeedFullyUploadedFileAsync();

        var result = await CreateService().GetEligibleFilesAsync(CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.TotalBytes.Should().Be(1_000_000);
        result.Items.Should().ContainSingle(i => i.LibraryFileId == fileId);

        var item = result.Items[0];
        item.VideoFileExists.Should().BeTrue();
        item.PreviewDirExists.Should().BeTrue();
        item.ThumbnailDirExists.Should().BeTrue();
    }

    [Fact]
    public async Task GetEligibleFiles_WhenPartialUpload_ExcludesFile()
    {
        await SeedPartiallyUploadedFileAsync(); // returns (fileId, folderPath) — discard both

        var result = await CreateService().GetEligibleFilesAsync(CancellationToken.None);

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEligibleFiles_WhenNothingOnDisk_ExcludesFile()
    {
        await SeedFullyUploadedFileAsync(
            createVideoOnDisk: false,
            createPreviewDir: false,
            createThumbnailDir: false);

        var result = await CreateService().GetEligibleFilesAsync(CancellationToken.None);

        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetEligibleFiles_WhenOnlyPreviewDirExists_IncludesFileWithZeroBytes()
    {
        await SeedFullyUploadedFileAsync(
            createVideoOnDisk: false,
            createPreviewDir: true,
            createThumbnailDir: false);

        var result = await CreateService().GetEligibleFilesAsync(CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.TotalBytes.Should().Be(0, "no video file on disk — nothing freed from video");

        var item = result.Items[0];
        item.VideoFileExists.Should().BeFalse();
        item.PreviewDirExists.Should().BeTrue();
        item.ThumbnailDirExists.Should().BeFalse();
    }

    // ── Delete tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteEligibleFiles_WhenFullyUploaded_DeletesAllFilesAndReturnsCorrectCounts()
    {
        var fileId = await SeedFullyUploadedFileAsync();
        var previewDir   = Path.Combine(_previewDir, fileId.ToString());
        var thumbnailDir = Path.Combine(_thumbnailDir, fileId.ToString());

        var result = await CreateService().DeleteEligibleFilesAsync(CancellationToken.None);

        result.DeletedCount.Should().Be(1);
        result.FreedBytes.Should().Be(1_000_000);

        // Video file gone — verify via preview result (no files on disk means count drops to 0)
        var afterPreview = await CreateService().GetEligibleFilesAsync(CancellationToken.None);
        afterPreview.TotalCount.Should().Be(0, "all disk files should be gone after delete");

        Directory.Exists(previewDir).Should().BeFalse();
        Directory.Exists(thumbnailDir).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteEligibleFiles_WhenPartialUpload_DoesNotDeleteFile()
    {
        var (_, folderPath) = await SeedPartiallyUploadedFileAsync();
        var videoPath = Path.Combine(folderPath, "partial.mp4");

        var result = await CreateService().DeleteEligibleFilesAsync(CancellationToken.None);

        result.DeletedCount.Should().Be(0);
        result.FreedBytes.Should().Be(0);
        File.Exists(videoPath).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteEligibleFiles_WhenMultipleFiles_DeletesAllEligible()
    {
        await SeedFullyUploadedFileAsync(videoFile: "video1.mp4");
        await SeedFullyUploadedFileAsync(videoFile: "video2.mp4");
        var (_, partialFolder) = await SeedPartiallyUploadedFileAsync();

        var result = await CreateService().DeleteEligibleFilesAsync(CancellationToken.None);

        result.DeletedCount.Should().Be(2);
        result.FreedBytes.Should().Be(2_000_000);
        File.Exists(Path.Combine(partialFolder, "partial.mp4")).Should().BeTrue("partial upload must not be deleted");
    }

    // ── SkippedPrdbAlreadyHasImages tests ─────────────────────────────────────

    [Fact]
    public async Task GetEligibleFiles_WhenPrdbAlreadyHadImages_ReturnsFile()
    {
        var fileId = await SeedSkippedFileAsync();

        var result = await CreateService().GetEligibleFilesAsync(CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.TotalBytes.Should().Be(1_000_000);
        result.Items.Should().ContainSingle(i => i.LibraryFileId == fileId);

        var item = result.Items[0];
        item.VideoFileExists.Should().BeTrue();
        item.PreviewDirExists.Should().BeTrue();
        item.ThumbnailDirExists.Should().BeTrue();
    }

    [Fact]
    public async Task GetEligibleFiles_WhenPrdbAlreadyHadImages_NothingOnDisk_ExcludesFile()
    {
        await SeedSkippedFileAsync(
            createVideoOnDisk: false,
            createPreviewDir: false,
            createThumbnailDir: false);

        var result = await CreateService().GetEligibleFilesAsync(CancellationToken.None);

        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteEligibleFiles_WhenPrdbAlreadyHadImages_DeletesFilesAndReturnsCorrectCounts()
    {
        var fileId = await SeedSkippedFileAsync();
        var previewDir   = Path.Combine(_previewDir, fileId.ToString());
        var thumbnailDir = Path.Combine(_thumbnailDir, fileId.ToString());

        var result = await CreateService().DeleteEligibleFilesAsync(CancellationToken.None);

        result.DeletedCount.Should().Be(1);
        result.FreedBytes.Should().Be(1_000_000);

        var afterPreview = await CreateService().GetEligibleFilesAsync(CancellationToken.None);
        afterPreview.TotalCount.Should().Be(0, "all disk files should be gone after delete");

        Directory.Exists(previewDir).Should().BeFalse();
        Directory.Exists(thumbnailDir).Should().BeFalse();
    }
}
