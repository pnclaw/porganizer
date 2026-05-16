using porganizer.Api.Features.DownloadClients;
using porganizer.Database;

namespace porganizer.Api.Tests.DownloadClients;

public sealed class DownloadFolderMapperTests
{
    [Fact]
    public void Apply_WhenPathIsExactMappedFolder_ReturnsMappedFolder()
    {
        var mapped = DownloadFolderMapper.Apply(
            "/downloads",
            [Mapping("/downloads", "/mnt/downloads")]);

        mapped.Should().Be("/mnt/downloads");
    }

    [Fact]
    public void Apply_WhenPathIsChildOfMappedFolder_ReturnsMappedChildPath()
    {
        var mapped = DownloadFolderMapper.Apply(
            "/downloads/release/video.mkv",
            [Mapping("/downloads", "/mnt/downloads")]);

        mapped.Should().Be("/mnt/downloads/release/video.mkv");
    }

    [Fact]
    public void Apply_WhenPathOnlySharesPrefix_DoesNotMap()
    {
        var mapped = DownloadFolderMapper.Apply(
            "/downloads2/release/video.mkv",
            [Mapping("/downloads", "/mnt/downloads")]);

        mapped.Should().Be("/downloads2/release/video.mkv");
    }

    [Fact]
    public void Apply_NormalizesTrailingSeparatorsBeforeMatching()
    {
        var mapped = DownloadFolderMapper.Apply(
            "/downloads/release/video.mkv",
            [Mapping("/downloads/", "/mnt/downloads/")]);

        mapped.Should().Be("/mnt/downloads/release/video.mkv");
    }

    [Fact]
    public void Apply_WhenExactPathHasTrailingSeparator_ReturnsMappedFolderWithoutTrailingSeparator()
    {
        var mapped = DownloadFolderMapper.Apply(
            "/downloads/",
            [Mapping("/downloads/", "/mnt/downloads/")]);

        mapped.Should().Be("/mnt/downloads");
    }

    [Fact]
    public void Apply_WhenMultipleMappingsMatch_UsesLongestOriginalFolder()
    {
        var mapped = DownloadFolderMapper.Apply(
            "/downloads/complete/release/video.mkv",
            [
                Mapping("/downloads", "/mnt/downloads"),
                Mapping("/downloads/complete", "/mnt/complete"),
            ]);

        mapped.Should().Be("/mnt/complete/release/video.mkv");
    }

    [Fact]
    public void Apply_MatchesBackslashChildPaths()
    {
        var mapped = DownloadFolderMapper.Apply(
            @"C:\Downloads\release\video.mkv",
            [Mapping(@"C:\Downloads", @"D:\Incoming")]);

        mapped.Should().Be(@"D:\Incoming\release\video.mkv");
    }

    private static FolderMapping Mapping(string original, string mappedTo) => new()
    {
        Id = Guid.NewGuid(),
        OriginalFolder = original,
        MappedToFolder = mappedTo,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };
}
