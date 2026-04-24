using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using porganizer.Database;
using porganizer.Database.Enums;
using porganizer.Api.Features.Prdb;

namespace porganizer.Api.Tests.Prdb;

public sealed class PrdbStatusTests : IAsyncLifetime
{
    private readonly PorganizerApiFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Get_ReturnsOk_WithZeroPreviewUploadCounts_WhenNoUploads()
    {
        var response = await _client.GetAsync("/api/prdb-status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrdbStatusResponse>();

        body.Should().NotBeNull();
        var upload = body!.PreviewImageUpload;
        upload.Should().NotBeNull();
        upload.IsEnabled.Should().BeFalse();
        upload.AutoDeleteEnabled.Should().BeFalse();
        upload.FilesUploaded.Should().Be(0);
        upload.ImagesUploaded.Should().Be(0);
        upload.FilesPending.Should().Be(0);
        upload.LastUploadedAt.Should().BeNull();
        upload.FilesAwaitingPreviewGeneration.Should().Be(0);
        upload.FilesAwaitingThumbnailGeneration.Should().Be(0);
    }

    [Fact]
    public async Task Get_CountsUploadedFilesAndImagesSeparately()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var site = new PrdbSite { Id = Guid.NewGuid(), Title = "Test Site", Url = "https://test.example", SyncedAtUtc = DateTime.UtcNow };
        db.PrdbSites.Add(site);

        var video = new PrdbVideo
        {
            Id = Guid.NewGuid(), Title = "Test Video", SiteId = site.Id,
            PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow,
        };
        db.PrdbVideos.Add(video);

        var folder = new LibraryFolder { Id = Guid.NewGuid(), Path = "/test/folder" };
        db.LibraryFolders.Add(folder);

        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;

        var file1 = new LibraryFile
        {
            Id              = Guid.NewGuid(),
            LibraryFolderId = folder.Id,
            RelativePath    = "a.mp4",
            FileSize        = 1000,
            VideoId         = video.Id,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow,
            SpriteSheetGeneratedAtUtc   = DateTime.UtcNow,
            LastSeenAtUtc   = DateTime.UtcNow,
            VideoUserImageUploadCompletedAtUtc = now,
            VideoUserImageUploadCompletionReason = VideoUserImageUploadCompletionReason.Uploaded,
            VideoUserImageUploadRemoteImageCount = 2,
        };
        var file2 = new LibraryFile
        {
            Id              = Guid.NewGuid(),
            LibraryFolderId = folder.Id,
            RelativePath    = "b.mp4",
            FileSize        = 2000,
            VideoId         = video.Id,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow,
            SpriteSheetGeneratedAtUtc   = DateTime.UtcNow,
            LastSeenAtUtc   = DateTime.UtcNow,
        };
        db.LibraryFiles.AddRange(file1, file2);
        await db.SaveChangesAsync();

        // file1 has 2 uploads (1 preview + 1 sprite)
        db.VideoUserImageUploads.AddRange(
            new VideoUserImageUpload
            {
                Id = Guid.NewGuid(), LibraryFileId = file1.Id, PrdbVideoId = video.Id,
                PrdbVideoUserImageId = Guid.NewGuid(), PreviewImageType = "Single",
                DisplayOrder = 0, UploadedAtUtc = now.AddMinutes(-2),
            },
            new VideoUserImageUpload
            {
                Id = Guid.NewGuid(), LibraryFileId = file1.Id, PrdbVideoId = video.Id,
                PrdbVideoUserImageId = Guid.NewGuid(), PreviewImageType = "SpriteSheet",
                DisplayOrder = 0, UploadedAtUtc = now.AddMinutes(-1),
            });

        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/prdb-status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrdbStatusResponse>();

        var upload = body!.PreviewImageUpload;
        upload.FilesUploaded.Should().Be(1);   // only file1 has uploads
        upload.ImagesUploaded.Should().Be(2);  // 2 rows total
        upload.FilesPending.Should().Be(1);    // file2 is eligible but not uploaded
        upload.LastUploadedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_DoesNotCountSkippedPrdbExistingImagesAsPending()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var site = new PrdbSite { Id = Guid.NewGuid(), Title = "Skip Site", Url = "https://skip.example", SyncedAtUtc = DateTime.UtcNow };
        db.PrdbSites.Add(site);

        var video = new PrdbVideo
        {
            Id = Guid.NewGuid(), Title = "Skip Video", SiteId = site.Id,
            PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow,
        };
        db.PrdbVideos.Add(video);

        var folder = new LibraryFolder { Id = Guid.NewGuid(), Path = "/test/skipped" };
        db.LibraryFolders.Add(folder);

        db.LibraryFiles.Add(new LibraryFile
        {
            Id = Guid.NewGuid(),
            LibraryFolderId = folder.Id,
            RelativePath = "skipped.mp4",
            FileSize = 1000,
            VideoId = video.Id,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow,
            PreviewImageCount = 5,
            SpriteSheetGeneratedAtUtc = DateTime.UtcNow,
            SpriteSheetTileCount = 100,
            LastSeenAtUtc = DateTime.UtcNow,
            VideoUserImageUploadCompletedAtUtc = DateTime.UtcNow,
            VideoUserImageUploadCompletionReason = VideoUserImageUploadCompletionReason.SkippedPrdbAlreadyHasImages,
            VideoUserImageUploadRemoteImageCount = 6,
        });

        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/prdb-status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrdbStatusResponse>();

        body!.PreviewImageUpload.FilesPending.Should().Be(0);
    }

    [Fact]
    public async Task Get_CountsPartialUploadsAsPending()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var site = new PrdbSite { Id = Guid.NewGuid(), Title = "Partial Site", Url = "https://partial.example", SyncedAtUtc = DateTime.UtcNow };
        db.PrdbSites.Add(site);

        var video = new PrdbVideo
        {
            Id = Guid.NewGuid(), Title = "Partial Video", SiteId = site.Id,
            PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow,
        };
        db.PrdbVideos.Add(video);

        var folder = new LibraryFolder { Id = Guid.NewGuid(), Path = "/test/partial" };
        db.LibraryFolders.Add(folder);

        var file = new LibraryFile
        {
            Id = Guid.NewGuid(),
            LibraryFolderId = folder.Id,
            RelativePath = "partial.mp4",
            FileSize = 1000,
            VideoId = video.Id,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow,
            PreviewImageCount = 5,
            SpriteSheetGeneratedAtUtc = DateTime.UtcNow,
            SpriteSheetTileCount = 100,
            LastSeenAtUtc = DateTime.UtcNow,
        };
        db.LibraryFiles.Add(file);

        db.VideoUserImageUploads.Add(new VideoUserImageUpload
        {
            Id = Guid.NewGuid(),
            LibraryFileId = file.Id,
            PrdbVideoId = video.Id,
            PrdbVideoUserImageId = Guid.NewGuid(),
            PreviewImageType = "Single",
            DisplayOrder = 0,
            UploadedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/prdb-status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrdbStatusResponse>();

        body!.PreviewImageUpload.FilesPending.Should().Be(1);
    }

    [Fact]
    public async Task Get_CountsFilesAwaitingGenerationByGeneratedAtFields()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var folder = new LibraryFolder { Id = Guid.NewGuid(), Path = "/test/gen-queue" };
        db.LibraryFolders.Add(folder);
        await db.SaveChangesAsync();

        // Neither preview nor thumbnail generated
        var fileNeither = new LibraryFile
        {
            Id = Guid.NewGuid(), LibraryFolderId = folder.Id, RelativePath = "gen1.mp4",
            FileSize = 100, LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = null, SpriteSheetGeneratedAtUtc = null,
        };
        // Preview generated, thumbnail not
        var filePreviewOnly = new LibraryFile
        {
            Id = Guid.NewGuid(), LibraryFolderId = folder.Id, RelativePath = "gen2.mp4",
            FileSize = 100, LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow, SpriteSheetGeneratedAtUtc = null,
        };
        // Both generated
        var fileBoth = new LibraryFile
        {
            Id = Guid.NewGuid(), LibraryFolderId = folder.Id, RelativePath = "gen3.mp4",
            FileSize = 100, LastSeenAtUtc = DateTime.UtcNow,
            PreviewImagesGeneratedAtUtc = DateTime.UtcNow, SpriteSheetGeneratedAtUtc = DateTime.UtcNow,
        };
        db.LibraryFiles.AddRange(fileNeither, filePreviewOnly, fileBoth);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/prdb-status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrdbStatusResponse>();

        var upload = body!.PreviewImageUpload;
        // only fileNeither lacks preview generation
        upload.FilesAwaitingPreviewGeneration.Should().Be(1);
        // fileNeither and filePreviewOnly both lack thumbnail generation
        upload.FilesAwaitingThumbnailGeneration.Should().Be(2);
    }

    [Fact]
    public async Task Get_DoesNotCountFilesPendingWhenGenerationIsIncomplete()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var site = new PrdbSite { Id = Guid.NewGuid(), Title = "Test Site 2", Url = "https://test2.example", SyncedAtUtc = DateTime.UtcNow };
        db.PrdbSites.Add(site);

        var video = new PrdbVideo
        {
            Id = Guid.NewGuid(), Title = "Test Video 2", SiteId = site.Id,
            PrdbCreatedAtUtc = DateTime.UtcNow, PrdbUpdatedAtUtc = DateTime.UtcNow, SyncedAtUtc = DateTime.UtcNow,
        };
        db.PrdbVideos.Add(video);

        var folder = new LibraryFolder { Id = Guid.NewGuid(), Path = "/test/folder2" };
        db.LibraryFolders.Add(folder);

        await db.SaveChangesAsync();

        // Matched but preview not yet generated
        var file = new LibraryFile
        {
            Id              = Guid.NewGuid(),
            LibraryFolderId = folder.Id,
            RelativePath    = "c.mp4",
            FileSize        = 500,
            VideoId         = video.Id,
            PreviewImagesGeneratedAtUtc = null,
            SpriteSheetGeneratedAtUtc   = null,
            LastSeenAtUtc   = DateTime.UtcNow,
        };
        db.LibraryFiles.Add(file);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/prdb-status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrdbStatusResponse>();

        body!.PreviewImageUpload.FilesPending.Should().Be(0);
    }
}
