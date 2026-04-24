using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace porganizer.Api.Features.Database;

[ApiController]
[Route("api/admin/database")]
[Produces("application/json")]
public class DatabaseController(IDatabaseViewService service) : ControllerBase
{
    [HttpGet("tables")]
    [EndpointSummary("List tables")]
    [EndpointDescription("Returns all user-defined table names in the local SQLite database.")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTables()
    {
        var tables = await service.GetTablesAsync();
        return Ok(tables);
    }

    [HttpGet("tables/{table}/rows")]
    [EndpointSummary("Query table rows")]
    [EndpointDescription("Returns a paginated result set for the given table. An optional WHERE clause may be supplied. The table name is validated against sqlite_master.")]
    [ProducesResponseType(typeof(DatabaseRowsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRows(
        string table,
        [FromQuery] string? where = null,
        [FromQuery] string? orderBy = null,
        [FromQuery] string? orderDir = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 1000) pageSize = 50;

        try
        {
            var result = await service.GetTableRowsAsync(table, where, orderBy, orderDir, page, pageSize);
            return Ok(new DatabaseRowsResponse(result.Columns, result.Rows, result.Total));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("query")]
    [Consumes("application/json")]
    [EndpointSummary("Execute raw SQL")]
    [EndpointDescription("Executes an arbitrary SQL statement against the local SQLite database. SELECT queries return columns and rows; mutations return rowsAffected.")]
    [ProducesResponseType(typeof(DatabaseQueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExecuteQuery([FromBody] DatabaseQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest("SQL must not be empty.");

        try
        {
            var result = await service.ExecuteRawQueryAsync(request.Sql);
            return Ok(new DatabaseQueryResponse(result.Columns, result.Rows, result.RowsAffected));
        }
        catch (SqliteException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

public sealed record DatabaseRowsResponse(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int Total);

public sealed record DatabaseQueryRequest(string Sql);

public sealed record DatabaseQueryResponse(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int RowsAffected);
