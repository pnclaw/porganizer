using Microsoft.Extensions.Options;

namespace porganizer.Api.Features.AppLogs;

public class AppLogsService(IOptions<AppLogsOptions> options) : IAppLogsService
{
    private string LogsDir => options.Value.LogsDirectory;

    public IReadOnlyList<AppLogFileInfo> ListFiles()
    {
        if (!Directory.Exists(LogsDir))
            return [];

        return [.. Directory.GetFiles(LogsDir, "*.log")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Name)
            .Select(f => new AppLogFileInfo(
                f.Name,
                f.LastWriteTimeUtc.ToString("yyyy-MM-dd"),
                f.Length))];
    }

    public AppLogLinesResponse GetLines(string filename, string? search, IReadOnlyList<string>? levels)
    {
        var path = ResolveSafePath(filename);

        if (!File.Exists(path)) throw new FileNotFoundException();

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        var all = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
            all.Add(line);

        IEnumerable<string> matched = all;

        if (!string.IsNullOrWhiteSpace(search))
            matched = matched.Where(l => l.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));

        if (levels is { Count: > 0 })
        {
            var tags = levels.Select(lv => $"[{lv.ToUpperInvariant()}]").ToList();
            matched = matched.Where(l => tags.Any(tag => l.Contains(tag, StringComparison.Ordinal)));
        }

        var matchedList = matched.ToList();
        return new AppLogLinesResponse(filename, matchedList, all.Count, matchedList.Count);
    }

    public int DeleteFiles(LogRetentionPolicy retain)
    {
        if (!Directory.Exists(LogsDir))
            return 0;

        var files = Directory.GetFiles(LogsDir, "*.log")
            .Select(f => new FileInfo(f))
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(-7);

        var toDelete = retain switch
        {
            LogRetentionPolicy.All   => files,
            LogRetentionPolicy.Last7 => files.Where(f => DateOnly.FromDateTime(f.LastWriteTimeUtc) < cutoff).ToList(),
            LogRetentionPolicy.Today => files.Where(f => DateOnly.FromDateTime(f.LastWriteTimeUtc) < today).ToList(),
            _                        => [],
        };

        var deleted = 0;
        foreach (var f in toDelete)
        {
            try { f.Delete(); deleted++; }
            catch { /* locked or missing — skip */ }
        }
        return deleted;
    }

    private string ResolveSafePath(string filename)
    {
        if (filename.Contains('/') || filename.Contains('\\') || filename.Contains(".."))
            throw new ArgumentException("Invalid filename.", nameof(filename));

        var resolved = Path.GetFullPath(Path.Combine(LogsDir, filename));
        var root     = Path.GetFullPath(LogsDir) + Path.DirectorySeparatorChar;

        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid filename.", nameof(filename));

        return resolved;
    }
}
