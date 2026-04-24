namespace porganizer.Api.Features.Database;

public interface IDatabaseViewService
{
    Task<IReadOnlyList<string>> GetTablesAsync();
    Task<DatabaseTableResult> GetTableRowsAsync(string table, string? where, string? orderBy, string? orderDir, int page, int pageSize);
    Task<RawQueryResult> ExecuteRawQueryAsync(string sql);
}

public sealed record DatabaseTableResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int Total);

public sealed record RawQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int RowsAffected);
