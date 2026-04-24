using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexerBackfillState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IndexerBackfillCompletedAtUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IndexerBackfillCurrentIndexerId",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IndexerBackfillCurrentOffset",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexerBackfillCutoffUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IndexerBackfillDays",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexerBackfillLastRunAtUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexerBackfillStartedAtUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "IndexerBackfillCompletedAtUtc", "IndexerBackfillCurrentIndexerId", "IndexerBackfillCurrentOffset", "IndexerBackfillCutoffUtc", "IndexerBackfillDays", "IndexerBackfillLastRunAtUtc", "IndexerBackfillStartedAtUtc" },
                values: new object[] { null, null, null, null, 30, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IndexerBackfillCompletedAtUtc",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillCurrentIndexerId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillCurrentOffset",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillCutoffUtc",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillDays",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillLastRunAtUtc",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillStartedAtUtc",
                table: "AppSettings");
        }
    }
}
