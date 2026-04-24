using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class RepairDownloadLogTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE DownloadLogs
                SET UpdatedAt = COALESCE(
                    CASE WHEN CompletedAt IS NOT NULL AND CompletedAt > '0001-01-02' THEN CompletedAt END,
                    CASE WHEN LastPolledAt IS NOT NULL AND LastPolledAt > '0001-01-02' THEN LastPolledAt END,
                    CURRENT_TIMESTAMP
                )
                WHERE UpdatedAt <= '0001-01-02';
                """);

            migrationBuilder.Sql(
                """
                UPDATE DownloadLogs
                SET CreatedAt = COALESCE(
                    CASE WHEN CompletedAt IS NOT NULL AND CompletedAt > '0001-01-02' THEN CompletedAt END,
                    CASE WHEN LastPolledAt IS NOT NULL AND LastPolledAt > '0001-01-02' THEN LastPolledAt END,
                    CASE WHEN UpdatedAt IS NOT NULL AND UpdatedAt > '0001-01-02' THEN UpdatedAt END,
                    CURRENT_TIMESTAMP
                )
                WHERE CreatedAt <= '0001-01-02';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
