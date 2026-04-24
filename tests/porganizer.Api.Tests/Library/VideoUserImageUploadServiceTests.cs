using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using porganizer.Api.Common.Prdb;
using porganizer.Api.Features.Library;
using porganizer.Api.Features.Library.VideoUserImageUpload;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.Library;

public sealed class VideoUserImageUploadServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly string _previewDir;
    private readonly string _thumbnailDir;
    private readonly string _tempRoot;

    public VideoUserImageUploadServiceTests()
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

        _tempRoot = Path.Combine(Path.GetTempPath(), $"porganizer-upload-test-{Guid.NewGuid()}");
        _previewDir = Path.Combine(_tempRoot, "previews");
        _thumbnailDir = Path.Combine(_tempRoot, "thumbnails");
        Directory.CreateDirectory(_previewDir);
        Directory.CreateDirectory(_thumbnailDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private VideoUserImageUploadService CreateService(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder,
        IPrdbUserImageCheckService? prdbCheck = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.prdb.test/") };

        return new VideoUserImageUploadService(
            _db,
            new StubHttpClientFactory(httpClient),
            prdbCheck ?? new StubPrdbUserImageCheckService(0),
            Options.Create(new PreviewOptions { CachePath = _previewDir }),
            Options.Create(new ThumbnailOptions { CachePath = _thumbnailDir }),
            NullLogger<VideoUserImageUploadService>.Instance);
    }

    private VideoUserImageUploadService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IPrdbUserImageCheckService? prdbCheck = null) =>
        CreateService(req => Task.FromResult(responder(req)), prdbCheck);

    private async Task<(Guid siteId, Guid videoId, Guid fileId)> SeedFullyGeneratedFileAsync()
    {
        var siteId = Guid.NewGuid();
        _db.PrdbSites.Add(new PrdbSite
        {
            Id = siteId,
            Title = "Test Site",
            Url = "https://example.test",
            SyncedAtUtc = DateTime.UtcNow,
        });

        var videoId = Guid.NewGuid();
        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id = videoId,
            SiteId = siteId,
            Title = "Test Video",
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        });

        var folderId = Guid.NewGuid();
        _db.LibraryFolders.Add(new LibraryFolder
        {
            Id = folderId,
            Path = "/tmp/test-library",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var fileId = Guid.NewGuid();
        _db.LibraryFiles.Add(new LibraryFile
        {
            Id = fileId,
            LibraryFolderId = folderId,
            RelativePath = "video.mp4",
            FileSize = 1_000_000,
            OsHash = "abcdef1234567890",
            VideoId = videoId,
            LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow,
            PreviewImageCount = 5,
            SpriteSheetGeneratedAtUtc = DateTime.UtcNow,
            SpriteSheetTileCount = 100,
        });

        await _db.SaveChangesAsync();
        return (siteId, videoId, fileId);
    }

    private void CreatePreviewFiles(Guid fileId)
    {
        var dir = Path.Combine(_previewDir, fileId.ToString());
        Directory.CreateDirectory(dir);
        for (var i = 1; i <= 5; i++)
            File.WriteAllBytes(Path.Combine(dir, $"preview_{i}.jpg"), [0xFF, 0xD8, 0xFF, 0xE0]);
    }

    private void CreateSpriteFiles(Guid fileId)
    {
        var dir = Path.Combine(_thumbnailDir, fileId.ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "sprite.jpg"), [0xFF, 0xD8, 0xFF, 0xE0]);
        File.WriteAllText(Path.Combine(dir, "sprite.vtt"), "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\n");
    }

    private void AddFullUploadRecords(Guid fileId, Guid videoId)
    {
        var now = DateTime.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            _db.VideoUserImageUploads.Add(new VideoUserImageUpload
            {
                Id = Guid.NewGuid(),
                LibraryFileId = fileId,
                PrdbVideoId = videoId,
                PrdbVideoUserImageId = Guid.NewGuid(),
                PreviewImageType = "Single",
                DisplayOrder = i,
                UploadedAtUtc = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        _db.VideoUserImageUploads.Add(new VideoUserImageUpload
        {
            Id = Guid.NewGuid(),
            LibraryFileId = fileId,
            PrdbVideoId = videoId,
            PrdbVideoUserImageId = Guid.NewGuid(),
            PreviewImageType = "SpriteSheet",
            DisplayOrder = 0,
            UploadedAtUtc = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static string EmptyUserImagesJson() =>
        JsonSerializer.Serialize(Array.Empty<object>());

    private static string SubmitResponseJson(Guid imageId) =>
        JsonSerializer.Serialize(new
        {
            videoUserImageId = imageId,
            moderationTargetId = Guid.NewGuid(),
            moderationStatus = "Pending",
            moderationVisibility = "Hidden",
        });

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_WhenBothGeneratedAndNoExistingImages_UploadsAllSixAndSavesRecords()
    {
        var (_, videoId, fileId) = await SeedFullyGeneratedFileAsync();
        CreatePreviewFiles(fileId);
        CreateSpriteFiles(fileId);

        var settings = await _db.AppSettings.SingleAsync();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.VideoUserImageUploadEnabled = true;
        await _db.SaveChangesAsync();

        // Capture raw body strings while streams are still open inside the handler
        var capturedMethods = new List<HttpMethod>();
        var capturedBodies = new List<string?>();

        var service = CreateService(async req =>
        {
            capturedMethods.Add(req.Method);
            capturedBodies.Add(req.Content is not null
                ? await req.Content.ReadAsStringAsync()
                : null);

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(SubmitResponseJson(Guid.NewGuid()), Encoding.UTF8, "application/json")
            };
        });

        await service.UploadAsync(fileId, CancellationToken.None);

        // 6 POSTs (prdb user-image check now handled by IPrdbUserImageCheckService, not the HTTP handler)
        capturedMethods.Should().HaveCount(6);
        capturedMethods.Should().AllSatisfy(m => m.Should().Be(HttpMethod.Post));

        // Single POSTs (indices 0–4): no VttFile field, no HasVtt field
        foreach (var body in capturedBodies.Take(5))
        {
            body.Should().NotContain("VttFile");
            body.Should().NotContain("HasVtt");
        }

        // SpriteSheet POST (index 5): includes VttFile, no HasVtt
        capturedBodies[5].Should().Contain("VttFile");
        capturedBodies[5].Should().NotContain("HasVtt");

        var uploads = await _db.VideoUserImageUploads.ToListAsync();
        uploads.Should().HaveCount(6);
        uploads.Count(u => u.PreviewImageType == "Single").Should().Be(5);
        uploads.Count(u => u.PreviewImageType == "SpriteSheet").Should().Be(1);
        uploads.Select(u => u.PrdbVideoId).Distinct().Should().ContainSingle().Which.Should().Be(videoId);

        var singles = uploads.Where(u => u.PreviewImageType == "Single").OrderBy(u => u.DisplayOrder).ToList();
        singles.Select(u => u.DisplayOrder).Should().Equal(0, 1, 2, 3, 4);

        var sprite = uploads.Single(u => u.PreviewImageType == "SpriteSheet");
        sprite.DisplayOrder.Should().Be(0);
    }

    // ── Skip: prdb already has images ─────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_WhenPrdbAlreadyHasImages_SkipsUpload()
    {
        var (_, _, fileId) = await SeedFullyGeneratedFileAsync();
        CreatePreviewFiles(fileId);
        CreateSpriteFiles(fileId);

        var settings = await _db.AppSettings.SingleAsync();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.VideoUserImageUploadEnabled = true;
        await _db.SaveChangesAsync();

        var postCount = 0;
        var service = CreateService(
            req =>
            {
                postCount++;
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(SubmitResponseJson(Guid.NewGuid()), Encoding.UTF8, "application/json")
                };
            },
            prdbCheck: new StubPrdbUserImageCheckService(1));

        await service.UploadAsync(fileId, CancellationToken.None);

        postCount.Should().Be(0);
        (await _db.VideoUserImageUploads.AnyAsync()).Should().BeFalse();

        var file = await _db.LibraryFiles.SingleAsync(f => f.Id == fileId);
        file.VideoUserImageUploadCompletedAtUtc.Should().NotBeNull();
        file.VideoUserImageUploadCompletionReason.Should().Be(VideoUserImageUploadCompletionReason.SkippedPrdbAlreadyHasImages);
        file.VideoUserImageUploadRemoteImageCount.Should().Be(1);
    }

    // ── Skip: already uploaded locally ────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_WhenAlreadyUploadedLocally_DoesNotCallPrdb()
    {
        var (_, videoId, fileId) = await SeedFullyGeneratedFileAsync();
        CreatePreviewFiles(fileId);
        CreateSpriteFiles(fileId);

        var settings = await _db.AppSettings.SingleAsync();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.VideoUserImageUploadEnabled = true;
        await _db.SaveChangesAsync();

        AddFullUploadRecords(fileId, videoId);
        await _db.SaveChangesAsync();

        var requestCount = 0;
        var service = CreateService(req =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EmptyUserImagesJson(), Encoding.UTF8, "application/json")
            };
        });

        await service.UploadAsync(fileId, CancellationToken.None);

        requestCount.Should().Be(0);
        (await _db.VideoUserImageUploads.CountAsync()).Should().Be(6); // unchanged

        var file = await _db.LibraryFiles.SingleAsync(f => f.Id == fileId);
        file.VideoUserImageUploadCompletedAtUtc.Should().NotBeNull();
        file.VideoUserImageUploadCompletionReason.Should().Be(VideoUserImageUploadCompletionReason.Uploaded);
        file.VideoUserImageUploadRemoteImageCount.Should().Be(6);
    }

    // ── Skip: upload disabled ─────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_WhenUploadDisabled_DoesNothing()
    {
        var (_, _, fileId) = await SeedFullyGeneratedFileAsync();
        CreatePreviewFiles(fileId);
        CreateSpriteFiles(fileId);

        // VideoUserImageUploadEnabled defaults to false
        var settings = await _db.AppSettings.SingleAsync();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        await _db.SaveChangesAsync();

        var requestCount = 0;
        var service = CreateService(req =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await service.UploadAsync(fileId, CancellationToken.None);

        requestCount.Should().Be(0);
        (await _db.VideoUserImageUploads.AnyAsync()).Should().BeFalse();
    }

    // ── Auto-delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_WhenAutoDeleteEnabled_DeletesVideoPreviewAndThumbnailAfterUpload()
    {
        var libraryFolderPath = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(libraryFolderPath);

        var (_, _, fileId) = await SeedFileInFolderAsync(libraryFolderPath, "video.mp4");

        // Create the actual video file on disk
        var videoPath = Path.Combine(libraryFolderPath, "video.mp4");
        File.WriteAllBytes(videoPath, [0x00, 0x01, 0x02]);

        CreatePreviewFiles(fileId);
        CreateSpriteFiles(fileId);

        var settings = await _db.AppSettings.SingleAsync();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.VideoUserImageUploadEnabled = true;
        settings.AutoDeleteAfterPreviewUpload = true;
        await _db.SaveChangesAsync();

        var service = CreateService(req =>
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(SubmitResponseJson(Guid.NewGuid()), Encoding.UTF8, "application/json")
            });

        await service.UploadAsync(fileId, CancellationToken.None);

        File.Exists(videoPath).Should().BeFalse("video file should be deleted after upload");
        Directory.Exists(Path.Combine(_previewDir, fileId.ToString())).Should().BeFalse("preview dir should be deleted after upload");
        Directory.Exists(Path.Combine(_thumbnailDir, fileId.ToString())).Should().BeFalse("thumbnail dir should be deleted after upload");
    }

    [Fact]
    public async Task UploadAsync_WhenAutoDeleteDisabled_LeavesFilesOnDisk()
    {
        var libraryFolderPath = Path.Combine(_tempRoot, "library2");
        Directory.CreateDirectory(libraryFolderPath);

        var (_, _, fileId) = await SeedFileInFolderAsync(libraryFolderPath, "video.mp4");

        var videoPath = Path.Combine(libraryFolderPath, "video.mp4");
        File.WriteAllBytes(videoPath, [0x00, 0x01, 0x02]);

        CreatePreviewFiles(fileId);
        CreateSpriteFiles(fileId);

        var settings = await _db.AppSettings.SingleAsync();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.VideoUserImageUploadEnabled = true;
        settings.AutoDeleteAfterPreviewUpload = false;
        await _db.SaveChangesAsync();

        var service = CreateService(req =>
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(SubmitResponseJson(Guid.NewGuid()), Encoding.UTF8, "application/json")
            });

        await service.UploadAsync(fileId, CancellationToken.None);

        File.Exists(videoPath).Should().BeTrue("video file should remain when auto-delete is off");
        Directory.Exists(Path.Combine(_previewDir, fileId.ToString())).Should().BeTrue("preview dir should remain when auto-delete is off");
        Directory.Exists(Path.Combine(_thumbnailDir, fileId.ToString())).Should().BeTrue("thumbnail dir should remain when auto-delete is off");
    }

    [Fact]
    public async Task UploadAsync_WhenAutoDeleteEnabledButSomeUploadsFail_LeavesFilesOnDisk()
    {
        var libraryFolderPath = Path.Combine(_tempRoot, "library3");
        Directory.CreateDirectory(libraryFolderPath);

        var (_, _, fileId) = await SeedFileInFolderAsync(libraryFolderPath, "video.mp4");

        var videoPath = Path.Combine(libraryFolderPath, "video.mp4");
        File.WriteAllBytes(videoPath, [0x00, 0x01, 0x02]);

        CreatePreviewFiles(fileId);
        CreateSpriteFiles(fileId);

        var settings = await _db.AppSettings.SingleAsync();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.VideoUserImageUploadEnabled = true;
        settings.AutoDeleteAfterPreviewUpload = true;
        await _db.SaveChangesAsync();

        // Fail the sprite sheet POST (and subsequent) with Conflict so we get a partial upload:
        // 5 singles succeed, sprite sheet fails — allUploaded will be false
        var postCount = 0;
        var service = CreateService(req =>
        {
            postCount++;
            // First 4 previews succeed; 5th preview and sprite sheet return Conflict (no retry)
            return postCount <= 4
                ? new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(SubmitResponseJson(Guid.NewGuid()), Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.Conflict);
        });

        await service.UploadAsync(fileId, CancellationToken.None);

        File.Exists(videoPath).Should().BeTrue("video file must remain when upload was only partially successful");
        Directory.Exists(Path.Combine(_previewDir, fileId.ToString())).Should().BeTrue("preview dir must remain when upload was only partially successful");
        Directory.Exists(Path.Combine(_thumbnailDir, fileId.ToString())).Should().BeTrue("thumbnail dir must remain when upload was only partially successful");
    }

    private async Task<(Guid siteId, Guid videoId, Guid fileId)> SeedFileInFolderAsync(string folderPath, string relativePath)
    {
        var siteId = Guid.NewGuid();
        _db.PrdbSites.Add(new PrdbSite
        {
            Id = siteId,
            Title = "Test Site",
            Url = "https://example.test",
            SyncedAtUtc = DateTime.UtcNow,
        });

        var videoId = Guid.NewGuid();
        _db.PrdbVideos.Add(new PrdbVideo
        {
            Id = videoId,
            SiteId = siteId,
            Title = "Test Video",
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        });

        var folderId = Guid.NewGuid();
        _db.LibraryFolders.Add(new LibraryFolder
        {
            Id = folderId,
            Path = folderPath,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var fileId = Guid.NewGuid();
        _db.LibraryFiles.Add(new LibraryFile
        {
            Id = fileId,
            LibraryFolderId = folderId,
            RelativePath = relativePath,
            FileSize = 3,
            OsHash = "abcdef1234567890",
            VideoId = videoId,
            LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow,
            PreviewImageCount = 5,
            SpriteSheetGeneratedAtUtc = DateTime.UtcNow,
            SpriteSheetTileCount = 100,
        });

        await _db.SaveChangesAsync();
        return (siteId, videoId, fileId);
    }

    // ── Unmatched file (no VideoId) ───────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_ForUnmatchedFile_Uploads5PreviewsWithoutVideoIdField()
    {
        var fileId = await SeedUnmatchedFileAsync();
        CreatePreviewFiles(fileId);

        var settings = await _db.AppSettings.SingleAsync();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.VideoUserImageUploadEnabled = true;
        await _db.SaveChangesAsync();

        var capturedBodies = new List<string?>();

        var service = CreateService(async req =>
        {
            capturedBodies.Add(req.Content is not null
                ? await req.Content.ReadAsStringAsync()
                : null);

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(SubmitResponseJson(Guid.NewGuid()), Encoding.UTF8, "application/json")
            };
        });

        await service.UploadAsync(fileId, CancellationToken.None);

        capturedBodies.Should().HaveCount(5);
        capturedBodies.Should().AllSatisfy(b =>
        {
            b.Should().NotContain("VideoId");
            b.Should().NotContain("VttFile");
        });

        var uploads = await _db.VideoUserImageUploads.ToListAsync();
        uploads.Should().HaveCount(5);
        uploads.Should().AllSatisfy(u => u.PreviewImageType.Should().Be("Single"));
        uploads.Should().AllSatisfy(u => u.PrdbVideoId.Should().BeNull());

        var file = await _db.LibraryFiles.SingleAsync(f => f.Id == fileId);
        file.VideoUserImageUploadCompletedAtUtc.Should().NotBeNull();
        file.VideoUserImageUploadCompletionReason.Should().Be(VideoUserImageUploadCompletionReason.Uploaded);
    }

    [Fact]
    public async Task UploadAsync_ForUnmatchedFile_WhenPrdbAlreadyHasImages_SkipsUpload()
    {
        var fileId = await SeedUnmatchedFileAsync();
        CreatePreviewFiles(fileId);

        var settings = await _db.AppSettings.SingleAsync();
        settings.PrdbApiKey = "test-key";
        settings.PrdbApiUrl = "https://api.prdb.test";
        settings.VideoUserImageUploadEnabled = true;
        await _db.SaveChangesAsync();

        var postCount = 0;
        var service = CreateService(
            req =>
            {
                postCount++;
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(SubmitResponseJson(Guid.NewGuid()), Encoding.UTF8, "application/json")
                };
            },
            prdbCheck: new StubPrdbUserImageCheckService(3));

        await service.UploadAsync(fileId, CancellationToken.None);

        postCount.Should().Be(0);
        (await _db.VideoUserImageUploads.AnyAsync()).Should().BeFalse();

        var file = await _db.LibraryFiles.SingleAsync(f => f.Id == fileId);
        file.VideoUserImageUploadCompletedAtUtc.Should().NotBeNull();
        file.VideoUserImageUploadCompletionReason.Should().Be(VideoUserImageUploadCompletionReason.SkippedPrdbAlreadyHasImages);
        file.VideoUserImageUploadRemoteImageCount.Should().Be(3);
    }

    private async Task<Guid> SeedUnmatchedFileAsync()
    {
        var folderId = Guid.NewGuid();
        _db.LibraryFolders.Add(new LibraryFolder
        {
            Id = folderId,
            Path = "/tmp/test-library-unmatched",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var fileId = Guid.NewGuid();
        _db.LibraryFiles.Add(new LibraryFile
        {
            Id = fileId,
            LibraryFolderId = folderId,
            RelativePath = "video.mp4",
            FileSize = 1_000_000,
            OsHash = "abcdef1234567890",
            VideoId = null,
            LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow,
            PreviewImageCount = 5,
            SpriteSheetGeneratedAtUtc = null,
            SpriteSheetTileCount = null,
        });

        await _db.SaveChangesAsync();
        return fileId;
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubPrdbUserImageCheckService(int? result) : IPrdbUserImageCheckService
    {
        public Task<int?> GetUserImageCountAsync(Guid videoId, AppSettings settings, CancellationToken ct) =>
            Task.FromResult(result);

        public Task<int?> GetUserImageCountByOsHashAsync(string osHash, AppSettings settings, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request);
    }
}
