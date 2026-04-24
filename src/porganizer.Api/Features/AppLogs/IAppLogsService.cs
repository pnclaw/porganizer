namespace porganizer.Api.Features.AppLogs;

public interface IAppLogsService
{
    IReadOnlyList<AppLogFileInfo> ListFiles();
    AppLogLinesResponse GetLines(string filename, string? search, IReadOnlyList<string>? levels);
    int DeleteFiles(LogRetentionPolicy retain);
}

public enum LogRetentionPolicy
{
    All   = 0,
    Last7 = 1,
    Today = 2,
}
