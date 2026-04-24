using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPrdbIndexerFilehashes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrdbIndexerFilehashBackfillPage",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrdbIndexerFilehashBackfillTotalCount",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrdbIndexerFilehashSyncCursorId",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrdbIndexerFilehashSyncCursorUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrdbIndexerFilehashes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IndexerSource = table.Column<int>(type: "INTEGER", nullable: false),
                    IndexerId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Filename = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    OsHash = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    PHash = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Filesize = table.Column<long>(type: "INTEGER", nullable: false),
                    SubmissionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PrdbCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PrdbUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbIndexerFilehashes", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PrdbIndexerFilehashBackfillPage", "PrdbIndexerFilehashBackfillTotalCount", "PrdbIndexerFilehashSyncCursorId", "PrdbIndexerFilehashSyncCursorUtc" },
                values: new object[] { 1, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_PrdbIndexerFilehashes_IndexerSource_IndexerId",
                table: "PrdbIndexerFilehashes",
                columns: new[] { "IndexerSource", "IndexerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrdbIndexerFilehashes_OsHash",
                table: "PrdbIndexerFilehashes",
                column: "OsHash");

            migrationBuilder.CreateIndex(
                name: "IX_PrdbIndexerFilehashes_PrdbUpdatedAtUtc",
                table: "PrdbIndexerFilehashes",
                column: "PrdbUpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrdbIndexerFilehashes");

            migrationBuilder.DropColumn(
                name: "PrdbIndexerFilehashBackfillPage",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PrdbIndexerFilehashBackfillTotalCount",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PrdbIndexerFilehashSyncCursorId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PrdbIndexerFilehashSyncCursorUtc",
                table: "AppSettings");
        }
    }
}
