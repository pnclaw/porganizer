namespace porganizer.Api.Features.AppLogs;

public sealed record AppLogLinesResponse(
    string Filename,
    IReadOnlyList<string> Lines,
    int TotalLines,
    int MatchedLines);
