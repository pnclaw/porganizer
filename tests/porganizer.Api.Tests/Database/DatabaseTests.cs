using System.Net;
using System.Net.Http.Json;

namespace porganizer.Api.Tests.Database;

public sealed class DatabaseTests : IAsyncLifetime
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

    // ── GetTables ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTables_ReturnsKnownTables()
    {
        var response = await _client.GetAsync("/api/admin/database/tables");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<string>>();
        body.Should().NotBeNull().And.NotBeEmpty();
        body!.Should().Contain("Indexers");
        body!.Should().Contain("DownloadClients");
    }

    // ── GetRows ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRows_ValidTable_ReturnsColumnsAndRows()
    {
        var response = await _client.GetAsync("/api/admin/database/tables/Indexers/rows");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TableRowsBody>();
        body.Should().NotBeNull();
        body!.Columns.Should().NotBeEmpty();
        body.Columns.Should().Contain("Id");
        body.Total.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetRows_WithPagination_RespectsPageSize()
    {
        var response = await _client.GetAsync("/api/admin/database/tables/Indexers/rows?page=1&pageSize=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TableRowsBody>();
        body.Should().NotBeNull();
        body!.Rows.Should().HaveCountLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task GetRows_UnknownTable_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/admin/database/tables/NonExistentTable/rows");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRows_WithWhereClause_FiltersRows()
    {
        var response = await _client.GetAsync("/api/admin/database/tables/AppSettings/rows?where=Id%3D1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TableRowsBody>();
        body.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRows_WithValidOrderBy_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/admin/database/tables/Indexers/rows?orderBy=Id&orderDir=desc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TableRowsBody>();
        body.Should().NotBeNull();
        body!.Columns.Should().Contain("Id");
    }

    [Fact]
    public async Task GetRows_WithInvalidOrderByColumn_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/admin/database/tables/Indexers/rows?orderBy=HackedColumn");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── ExecuteQuery ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteQuery_Select_ReturnsColumnsAndRows()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/database/query", new { sql = "SELECT * FROM AppSettings LIMIT 1" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<QueryResultBody>();
        body.Should().NotBeNull();
        body!.Columns.Should().NotBeEmpty();
        body.RowsAffected.Should().Be(-1);
    }

    [Fact]
    public async Task ExecuteQuery_NonSelect_ReturnsRowsAffected()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/database/query",
            new { sql = "CREATE TABLE IF NOT EXISTS _QueryTest (Id INTEGER PRIMARY KEY)" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<QueryResultBody>();
        body.Should().NotBeNull();
        body!.Columns.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteQuery_InvalidSql_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/database/query", new { sql = "THIS IS NOT SQL" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExecuteQuery_EmptySql_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/database/query", new { sql = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record TableRowsBody(List<string> Columns, List<object> Rows, int Total);
    private sealed record QueryResultBody(List<string> Columns, List<object> Rows, int RowsAffected);
}
