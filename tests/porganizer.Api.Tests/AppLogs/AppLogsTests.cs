using System.Net;
using System.Net.Http.Json;

namespace porganizer.Api.Tests.AppLogs;

public sealed class AppLogsTests : IAsyncLifetime
{
    private readonly PorganizerApiFactory _factory = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_NoFiles_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/api/app-logs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<LogFileItem>>();
        body.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task List_WithFiles_ReturnsFileInfo()
    {
        WriteLogFile("app-20260418.log", "line1\nline2\n");
        WriteLogFile("app-20260417.log", "older\n");

        var response = await _client.GetAsync("/api/app-logs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<LogFileItem>>();
        body.Should().NotBeNull();
        body!.Should().HaveCount(2);
        body![0].Filename.Should().Be("app-20260418.log");
        body[0].SizeBytes.Should().BeGreaterThan(0);
    }

    // ── GetLines ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLines_ExistingFile_ReturnsAllLines()
    {
        WriteLogFile("app-20260418.log", "alpha line\nbeta line\ngamma line\n");

        var response = await _client.GetAsync("/api/app-logs/app-20260418.log");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LogLinesBody>();
        body.Should().NotBeNull();
        body!.TotalLines.Should().Be(3);
        body.MatchedLines.Should().Be(3);
        body.Lines.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetLines_WithSearch_ReturnsMatchingLinesOnly()
    {
        WriteLogFile("app-20260418.log", "alpha line\nbeta line\ngamma line\n");

        var response = await _client.GetAsync("/api/app-logs/app-20260418.log?search=beta");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LogLinesBody>();
        body!.TotalLines.Should().Be(3);
        body.MatchedLines.Should().Be(1);
        body.Lines.Should().ContainSingle().Which.Should().Contain("beta");
    }

    [Fact]
    public async Task GetLines_WithLevelFilter_ReturnsMatchingLevelsOnly()
    {
        WriteLogFile("app-20260418.log",
            "2026-04-18 12:00:00 [INF] Info message\n" +
            "2026-04-18 12:00:01 [WRN] Warning message\n" +
            "2026-04-18 12:00:02 [ERR] Error message\n");

        var response = await _client.GetAsync("/api/app-logs/app-20260418.log?level=WRN&level=ERR");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LogLinesBody>();
        body!.TotalLines.Should().Be(3);
        body.MatchedLines.Should().Be(2);
        body.Lines.Should().HaveCount(2);
        body.Lines.Should().AllSatisfy(l => l.Should().MatchRegex(@"\[(WRN|ERR)\]"));
    }

    [Fact]
    public async Task GetLines_WithSearchAndLevel_CombinesFilters()
    {
        WriteLogFile("app-20260418.log",
            "2026-04-18 12:00:00 [INF] Info about foo\n" +
            "2026-04-18 12:00:01 [WRN] Warning about foo\n" +
            "2026-04-18 12:00:02 [WRN] Warning about bar\n");

        var response = await _client.GetAsync("/api/app-logs/app-20260418.log?search=foo&level=WRN");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LogLinesBody>();
        body!.MatchedLines.Should().Be(1);
        body.Lines.Should().ContainSingle().Which.Should().Contain("Warning about foo");
    }

    [Fact]
    public async Task GetLines_MissingFile_Returns404()
    {
        var response = await _client.GetAsync("/api/app-logs/app-99991231.log");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetLines_PathTraversal_Returns400()
    {
        var response = await _client.GetAsync("/api/app-logs/..%2Fetc%2Fpasswd");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_All_RemovesAllFiles()
    {
        WriteLogFile("app-20260418.log", "a\n");
        WriteLogFile("app-20260410.log", "b\n");

        var response = await _client.DeleteAsync("/api/app-logs?retain=all");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Directory.GetFiles(_factory.LogsPath, "*.log").Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_Today_RemovesOlderFiles()
    {
        var todayName  = $"app-{DateTime.UtcNow:yyyyMMdd}.log";
        var olderName  = "app-20200101.log";
        WriteLogFile(todayName,  "today\n");
        WriteLogFile(olderName, "old\n");
        File.SetLastWriteTimeUtc(
            Path.Combine(_factory.LogsPath, olderName),
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var response = await _client.DeleteAsync("/api/app-logs?retain=today");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var remaining = Directory.GetFiles(_factory.LogsPath, "*.log").Select(Path.GetFileName).ToList();
        remaining.Should().ContainSingle().Which.Should().Be(todayName);
    }

    [Fact]
    public async Task Delete_Last7_RemovesFilesOlderThan7Days()
    {
        var recentName = $"app-{DateTime.UtcNow:yyyyMMdd}.log";
        var olderName  = "app-20200101.log";
        WriteLogFile(recentName, "recent\n");
        WriteLogFile(olderName, "old\n");

        // backdate the old file's write time so it is clearly outside the 7-day window
        File.SetLastWriteTimeUtc(
            Path.Combine(_factory.LogsPath, olderName),
            DateTime.UtcNow.AddDays(-8));

        var response = await _client.DeleteAsync("/api/app-logs?retain=last7");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var remaining = Directory.GetFiles(_factory.LogsPath, "*.log").Select(Path.GetFileName).ToList();
        remaining.Should().ContainSingle().Which.Should().Be(recentName);
    }

    [Fact]
    public async Task Delete_InvalidRetain_Returns400()
    {
        var response = await _client.DeleteAsync("/api/app-logs?retain=bogus");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void WriteLogFile(string filename, string content)
    {
        Directory.CreateDirectory(_factory.LogsPath);
        File.WriteAllText(Path.Combine(_factory.LogsPath, filename), content);
    }

    private sealed record LogFileItem(string Filename, string Date, long SizeBytes);
    private sealed record LogLinesBody(string Filename, List<string> Lines, int TotalLines, int MatchedLines);
}
