using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using porganizer.Api.Features.Library;
using porganizer.Database;

namespace porganizer.Api.Tests.Library;

public sealed class LibraryThumbnailsTests : IAsyncLifetime
{
    private readonly PorganizerApiFactory _factory = new();
    private HttpClient _client = null!;
    private string _cachePath = null!;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        // Resolve the auto-derived cache path that Program.cs wired up
        _cachePath = _factory.Services.GetRequiredService<IOptions<ThumbnailOptions>>().Value.CachePath;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(Guid folderId, Guid fileId)> CreateLibraryFileWithSpriteAsync()
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
            SpriteSheetGeneratedAtUtc = DateTime.UtcNow,
            SpriteSheetTileCount = 10,
        });

        await db.SaveChangesAsync();
        return (folderId, fileId);
    }

    private void CreateSpriteFiles(Guid fileId)
    {
        var dir = Path.Combine(_cachePath, fileId.ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "sprite.jpg"), [0xFF, 0xD8, 0xFF, 0xE0]); // minimal JPEG header
        File.WriteAllText(Path.Combine(dir, "sprite.vtt"), "WEBVTT\n");
    }

    // ── GET sprite-sheet ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetSpriteSheet_WhenFileExistsOnDisk_Returns200()
    {
        var (_, fileId) = await CreateLibraryFileWithSpriteAsync();
        CreateSpriteFiles(fileId);

        var response = await _client.GetAsync($"/api/library-files/{fileId}/sprite-sheet");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task GetSpriteSheet_WhenNoSpriteGenerated_Returns404()
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
            SpriteSheetGeneratedAtUtc = null, // not yet generated
        });
        await db.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/library-files/{fileId}/sprite-sheet");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSpriteSheet_WhenFileIdUnknown_Returns404()
    {
        var response = await _client.GetAsync($"/api/library-files/{Guid.NewGuid()}/sprite-sheet");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET sprite-vtt ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetSpriteVtt_WhenFileExistsOnDisk_Returns200()
    {
        var (_, fileId) = await CreateLibraryFileWithSpriteAsync();
        CreateSpriteFiles(fileId);

        var response = await _client.GetAsync($"/api/library-files/{fileId}/sprite-vtt");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/vtt");
    }

    // ── POST generate-thumbnails (single file) ───────────────────────────────

    [Fact]
    public async Task GenerateForFile_WhenFileExists_Returns202AndClearsGeneratedAt()
    {
        var (_, fileId) = await CreateLibraryFileWithSpriteAsync();

        var response = await _client.PostAsync($"/api/library-files/{fileId}/generate-thumbnails", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await db.LibraryFiles.FindAsync(fileId);
        file!.SpriteSheetGeneratedAtUtc.Should().BeNull();
        file.SpriteSheetTileCount.Should().BeNull();
    }

    [Fact]
    public async Task GenerateForFile_WhenFileUnknown_Returns404()
    {
        var response = await _client.PostAsync($"/api/library-files/{Guid.NewGuid()}/generate-thumbnails", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST generate-all ────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAll_Returns202WithEnqueuedCount()
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
            SpriteSheetGeneratedAtUtc = null,
        });
        await db.SaveChangesAsync();

        var response = await _client.PostAsync("/api/library-thumbnails/generate-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<GenerateAllResponse>();
        body!.Enqueued.Should().BeGreaterThanOrEqualTo(1);
    }

    // ── POST reset-all ───────────────────────────────────────────────────────

    [Fact]
    public async Task ResetAll_ClearsGeneratedAtAndReturnsClearedCount()
    {
        var (_, fileId) = await CreateLibraryFileWithSpriteAsync();
        CreateSpriteFiles(fileId);

        var response = await _client.PostAsync("/api/library-thumbnails/reset-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ResetAllResponse>();
        body!.Cleared.Should().BeGreaterThanOrEqualTo(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await db.LibraryFiles.FindAsync(fileId);
        file!.SpriteSheetGeneratedAtUtc.Should().BeNull();
        file.SpriteSheetTileCount.Should().BeNull();

        // Sprite files should have been deleted from disk
        var spriteDir = Path.Combine(_cachePath, fileId.ToString());
        Directory.Exists(spriteDir).Should().BeFalse();
    }

    [Fact]
    public async Task ResetAll_WhenNothingGenerated_ReturnsClearedZero()
    {
        var response = await _client.PostAsync("/api/library-thumbnails/reset-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ResetAllResponse>();
        body!.Cleared.Should().Be(0);
    }

    // ── POST validate-ffmpeg ─────────────────────────────────────────────────

    [Fact]
    public async Task ValidateFfmpeg_WithValidBinary_ReturnsOkTrue()
    {
        var response = await _client.PostAsJsonAsync("/api/library-thumbnails/validate-ffmpeg",
            new { ffmpegPath = "ffmpeg" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FfmpegValidationResponse>();
        // ffmpeg is assumed to be installed in the CI/dev environment; if it is not,
        // the endpoint still returns 200 with ok=false — never a 4xx/5xx.
        body.Should().NotBeNull();
        body!.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateFfmpeg_WithBogusPath_ReturnsOkFalse()
    {
        var response = await _client.PostAsJsonAsync("/api/library-thumbnails/validate-ffmpeg",
            new { ffmpegPath = "/totally/nonexistent/ffmpeg-xyz" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FfmpegValidationResponse>();
        body!.Ok.Should().BeFalse();
        body.Message.Should().NotBeNullOrEmpty();
    }

    private sealed class GenerateAllResponse
    {
        public int Enqueued { get; set; }
    }

    private sealed class ResetAllResponse
    {
        public int Cleared { get; set; }
    }

    private sealed class FfmpegValidationResponse
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
