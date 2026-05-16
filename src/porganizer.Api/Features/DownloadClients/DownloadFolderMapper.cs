using porganizer.Database;

namespace porganizer.Api.Features.DownloadClients;

public static class DownloadFolderMapper
{
    private static readonly char[] DirectorySeparators = ['/', '\\'];

    public static string Apply(string path, IEnumerable<FolderMapping> mappings)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var normalizedPath = NormalizeForComparison(path);

        foreach (var mapping in mappings
                     .Where(m => !string.IsNullOrWhiteSpace(m.OriginalFolder))
                     .OrderByDescending(m => NormalizeForComparison(m.OriginalFolder).Length))
        {
            var original = NormalizeForComparison(mapping.OriginalFolder);
            if (!IsSameOrChildPath(normalizedPath, original))
                continue;

            var mappedTo = TrimTrailingSeparators(mapping.MappedToFolder.Trim());
            if (string.Equals(normalizedPath, original, StringComparison.OrdinalIgnoreCase))
                return mappedTo;

            var suffix = path[MatchedLength(path, original)..];
            return mappedTo + suffix;
        }

        return path;
    }

    private static bool IsSameOrChildPath(string candidatePath, string parentPath)
    {
        if (string.Equals(candidatePath, parentPath, StringComparison.OrdinalIgnoreCase))
            return true;

        if (candidatePath.Length <= parentPath.Length)
            return false;

        if (!candidatePath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase))
            return false;

        return DirectorySeparators.Contains(candidatePath[parentPath.Length]);
    }

    private static int MatchedLength(string path, string normalizedOriginal)
    {
        var trimmedLength = path.Trim().Length;
        while (trimmedLength > 0 && DirectorySeparators.Contains(path[trimmedLength - 1]))
            trimmedLength--;

        return Math.Min(trimmedLength, normalizedOriginal.Length);
    }

    private static string NormalizeForComparison(string path)
        => TrimTrailingSeparators(path.Trim());

    private static string TrimTrailingSeparators(string path)
    {
        while (path.Length > 1 && DirectorySeparators.Contains(path[^1]))
            path = path[..^1];

        return path;
    }
}
