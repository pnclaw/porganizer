using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using porganizer.Database;

namespace porganizer.Api.Features.Database;

public sealed class DatabaseViewService(AppDbContext db) : IDatabaseViewService
{
    public async Task<IReadOnlyList<string>> GetTablesAsync()
    {
        var conn = (SqliteConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

        var tables = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));

        return tables;
    }

    public async Task<DatabaseTableResult> GetTableRowsAsync(string table, string? where, string? orderBy, string? orderDir, int page, int pageSize)
    {
        var validTables = await GetTablesAsync();
        if (!validTables.Contains(table))
            throw new ArgumentException($"Unknown table: {table}");

        var conn = (SqliteConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(conn);

        var validColumns = await GetColumnNamesAsync(conn, table);
        string? orderByClause = null;
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            if (!validColumns.Contains(orderBy))
                throw new ArgumentException($"Unknown column: {orderBy}");
            var dir = string.Equals(orderDir, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
            orderByClause = $" ORDER BY \"{orderBy}\" {dir}";
        }

        var whereClause = string.IsNullOrWhiteSpace(where) ? "" : $" WHERE {where}";
        var quotedTable = $"\"{table}\"";

        int total;
        using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $"SELECT COUNT(*) FROM {quotedTable}{whereClause}";
            total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT * FROM {quotedTable}{whereClause}{orderByClause} LIMIT {pageSize} OFFSET {(page - 1) * pageSize}";
            using var reader = await cmd.ExecuteReaderAsync();

            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(i => reader.GetName(i))
                .ToList();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                    row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }

            return new DatabaseTableResult(columns, rows, total);
        }
    }

    public async Task<RawQueryResult> ExecuteRawQueryAsync(string sql)
    {
        var conn = (SqliteConnection)db.Database.GetDbConnection();
        await EnsureOpenAsync(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = await cmd.ExecuteReaderAsync();

        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.GetName(i))
            .ToList();

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
                row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return new RawQueryResult(columns, rows, reader.RecordsAffected);
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            cols.Add(reader.GetString(1)); // column 1 is "name"
        return cols;
    }

    private static async Task EnsureOpenAsync(IDbConnection conn)
    {
        if (conn.State != ConnectionState.Open)
            await ((SqliteConnection)conn).OpenAsync();
    }
}
