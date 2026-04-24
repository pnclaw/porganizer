using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using porganizer.Api.Features.Library;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Tests.Library;

public sealed class LibraryPreviewsTests : IAsyncLifetime
{
    private readonly PorganizerApiFactory _factory = new();
    private HttpClient _client = null!;
    private string _cachePath = null!;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _cachePath = _factory.Services.GetRequiredService<IOptions<PreviewOptions>>().Value.CachePath;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(Guid folderId, Guid fileId)> CreateLibraryFileWithPreviewsAsync(int previewCount = 5)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var folderId = Guid.NewGuid();
        db.LibraryFolders.Add(new LibraryFolder
        {
            Id = folderId,
            Path = "/tmp/test-library",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var fileId = Guid.NewGuid();
        db.LibraryFiles.Add(new LibraryFile
        {
            Id = fileId,
            LibraryFolderId = folderId,
            RelativePath = "video.mp4",
            FileSize = 1_000_000,
            LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow,
            PreviewImageCount = previewCount,
        });

        await db.SaveChangesAsync();
        return (folderId, fileId);
    }

    private void CreatePreviewFiles(Guid fileId, int count = 5)
    {
        var dir = Path.Combine(_cachePath, fileId.ToString());
        Directory.CreateDirectory(dir);
        for (var i = 1; i <= count; i++)
            File.WriteAllBytes(Path.Combine(dir, $"preview_{i}.jpg"), [0xFF, 0xD8, 0xFF, 0xE0]); // minimal JPEG header
    }

    // ── GET preview image ────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task GetPreviewImage_WhenFileExistsOnDisk_Returns200(int n)
    {
        var (_, fileId) = await CreateLibraryFileWithPreviewsAsync();
        CreatePreviewFiles(fileId);

        var response = await _client.GetAsync($"/api/library-files/{fileId}/previews/{n}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task GetPreviewImage_WhenNoPreviewsGenerated_Returns404()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var folderId = Guid.NewGuid();
        db.LibraryFolders.Add(new LibraryFolder
        {
            Id = folderId,
            Path = "/tmp/test-library",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        var fileId = Guid.NewGuid();
        db.LibraryFiles.Add(new LibraryFile
        {
            Id = fileId,
            LibraryFolderId = folderId,
            RelativePath = "video.mp4",
            FileSize = 1_000_000,
            LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = null,
        });
        await db.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/library-files/{fileId}/previews/1");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPreviewImage_WhenFileIdUnknown_Returns404()
    {
        var response = await _client.GetAsync($"/api/library-files/{Guid.NewGuid()}/previews/1");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task GetPreviewImage_WhenNOutOfRange_Returns404(int n)
    {
        var (_, fileId) = await CreateLibraryFileWithPreviewsAsync();
        CreatePreviewFiles(fileId);

        var response = await _client.GetAsync($"/api/library-files/{fileId}/previews/{n}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPreviewImage_WhenNExceedsActualCount_Returns404()
    {
        // Only 3 previews generated, but requesting preview 4
        var (_, fileId) = await CreateLibraryFileWithPreviewsAsync(previewCount: 3);
        CreatePreviewFiles(fileId, count: 3);

        var response = await _client.GetAsync($"/api/library-files/{fileId}/previews/4");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST generate-all previews ───────────────────────────────────────────

    [Fact]
    public async Task GenerateAllPreviews_Returns202WithEnqueuedCount()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var folderId = Guid.NewGuid();
        db.LibraryFolders.Add(new LibraryFolder
        {
            Id = folderId,
            Path = "/tmp/test-library",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        db.LibraryFiles.Add(new LibraryFile
        {
            Id = Guid.NewGuid(),
            LibraryFolderId = folderId,
            RelativePath = "video.mp4",
            FileSize = 1_000_000,
            LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = null,
        });
        await db.SaveChangesAsync();

        var response = await _client.PostAsync("/api/library-previews/generate-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<GenerateAllPreviewsResponse>();
        body!.Enqueued.Should().BeGreaterThanOrEqualTo(1);
    }

    // ── POST reset-all previews ──────────────────────────────────────────────

    [Fact]
    public async Task ResetAllPreviews_ClearsGeneratedAtAndReturnsClearedCount()
    {
        var (_, fileId) = await CreateLibraryFileWithPreviewsAsync();
        CreatePreviewFiles(fileId);

        var response = await _client.PostAsync("/api/library-previews/reset-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ResetAllPreviewsResponse>();
        body!.Cleared.Should().BeGreaterThanOrEqualTo(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await db.LibraryFiles.FindAsync(fileId);
        file!.PreviewImagesGeneratedAtUtc.Should().BeNull();
        file.PreviewImageCount.Should().BeNull();

        var previewDir = Path.Combine(_cachePath, fileId.ToString());
        Directory.Exists(previewDir).Should().BeFalse();
    }

    [Fact]
    public async Task ResetAllPreviews_WhenNothingGenerated_ReturnsClearedZero()
    {
        var response = await _client.PostAsync("/api/library-previews/reset-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ResetAllPreviewsResponse>();
        body!.Cleared.Should().Be(0);
    }

    // ── POST upload-all previews ─────────────────────────────────────────────

    [Fact]
    public async Task UploadAllPreviews_EnqueuesEligibleFiles()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var siteId = Guid.NewGuid();
        db.PrdbSites.Add(new PrdbSite { Id = siteId, Title = "S", Url = "https://s.test", SyncedAtUtc = DateTime.UtcNow });
        var videoId = Guid.NewGuid();
        db.PrdbVideos.Add(new PrdbVideo { Id = videoId, SiteId = siteId, Title = "V",
            PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow });

        var folderId = Guid.NewGuid();
        db.LibraryFolders.Add(new LibraryFolder { Id = folderId, Path = "/tmp/lib",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        // Eligible: both generated, matched, has OsHash, not yet uploaded
        db.LibraryFiles.Add(new LibraryFile
        {
            Id = Guid.NewGuid(), LibraryFolderId = folderId, RelativePath = "a.mp4", FileSize = 1,
            OsHash = "abcdef1234567890", VideoId = videoId, LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow, PreviewImageCount = 5,
            SpriteSheetGeneratedAtUtc = DateTime.UtcNow, SpriteSheetTileCount = 100,
        });

        // Ineligible: preview missing
        db.LibraryFiles.Add(new LibraryFile
        {
            Id = Guid.NewGuid(), LibraryFolderId = folderId, RelativePath = "b.mp4", FileSize = 1,
            OsHash = "bbbbbbbbbbbbbbbb", VideoId = videoId, LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = null,
            SpriteSheetGeneratedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        var response = await _client.PostAsync("/api/library-previews/upload-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UploadAllPreviewsResponse>();
        body!.Enqueued.Should().Be(1);
    }

    [Fact]
    public async Task UploadAllPreviews_SkipsAlreadyUploadedFiles()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var siteId = Guid.NewGuid();
        db.PrdbSites.Add(new PrdbSite { Id = siteId, Title = "S", Url = "https://s.test", SyncedAtUtc = DateTime.UtcNow });
        var videoId = Guid.NewGuid();
        db.PrdbVideos.Add(new PrdbVideo { Id = videoId, SiteId = siteId, Title = "V",
            PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow });

        var folderId = Guid.NewGuid();
        db.LibraryFolders.Add(new LibraryFolder { Id = folderId, Path = "/tmp/lib2",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        var fileId = Guid.NewGuid();
        db.LibraryFiles.Add(new LibraryFile
        {
            Id = fileId, LibraryFolderId = folderId, RelativePath = "a.mp4", FileSize = 1,
            OsHash = "cccccccccccccccc", VideoId = videoId, LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow, PreviewImageCount = 5,
            SpriteSheetGeneratedAtUtc = DateTime.UtcNow, SpriteSheetTileCount = 100,
            VideoUserImageUploadCompletedAtUtc = DateTime.UtcNow,
            VideoUserImageUploadCompletionReason = VideoUserImageUploadCompletionReason.Uploaded,
            VideoUserImageUploadRemoteImageCount = 1,
        });

        db.VideoUserImageUploads.Add(new VideoUserImageUpload
        {
            Id = Guid.NewGuid(), LibraryFileId = fileId, PrdbVideoId = videoId,
            PrdbVideoUserImageId = Guid.NewGuid(), PreviewImageType = "Single", DisplayOrder = 0,
            UploadedAtUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        var response = await _client.PostAsync("/api/library-previews/upload-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UploadAllPreviewsResponse>();
        body!.Enqueued.Should().Be(0);
    }

    [Fact]
    public async Task UploadAllPreviews_SkipsFilesCompletedBecausePrdbAlreadyHasImages()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var siteId = Guid.NewGuid();
        db.PrdbSites.Add(new PrdbSite { Id = siteId, Title = "S2", Url = "https://s2.test", SyncedAtUtc = DateTime.UtcNow });
        var videoId = Guid.NewGuid();
        db.PrdbVideos.Add(new PrdbVideo
        {
            Id = videoId,
            SiteId = siteId,
            Title = "V2",
            PrdbCreatedAtUtc = DateTime.UtcNow,
            PrdbUpdatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow,
        });

        var folderId = Guid.NewGuid();
        db.LibraryFolders.Add(new LibraryFolder
        {
            Id = folderId,
            Path = "/tmp/lib-skipped",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        db.LibraryFiles.Add(new LibraryFile
        {
            Id = Guid.NewGuid(),
            LibraryFolderId = folderId,
            RelativePath = "skipped.mp4",
            FileSize = 1,
            OsHash = "dddddddddddddddd",
            VideoId = videoId,
            LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow,
            PreviewImageCount = 5,
            SpriteSheetGeneratedAtUtc = DateTime.UtcNow,
            SpriteSheetTileCount = 100,
            VideoUserImageUploadCompletedAtUtc = DateTime.UtcNow,
            VideoUserImageUploadCompletionReason = VideoUserImageUploadCompletionReason.SkippedPrdbAlreadyHasImages,
            VideoUserImageUploadRemoteImageCount = 6,
        });

        await db.SaveChangesAsync();

        var response = await _client.PostAsync("/api/library-previews/upload-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UploadAllPreviewsResponse>();
        body!.Enqueued.Should().Be(0);
    }

    private sealed class GenerateAllPreviewsResponse
    {
        public int Enqueued { get; set; }
    }

    private sealed class ResetAllPreviewsResponse
    {
        public int Cleared { get; set; }
    }

    private sealed class UploadAllPreviewsResponse
    {
        public int Enqueued { get; set; }
    }
}
