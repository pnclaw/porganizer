using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPrdbDownloadedFromIndexerSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PrdbDownloadedFromIndexerId",
                table: "DownloadLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrdbDownloadedFromIndexerSyncAttemptedAtUtc",
                table: "DownloadLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrdbDownloadedFromIndexerSyncError",
                table: "DownloadLogs",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrdbDownloadedFromIndexerSyncFingerprint",
                table: "DownloadLogs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrdbDownloadedFromIndexerSyncedAtUtc",
                table: "DownloadLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "DownloadLogFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "PrdbDownloadedFromIndexerFilenameId",
                table: "DownloadLogFiles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrdbDownloadedFromIndexerId",
                table: "DownloadLogs");

            migrationBuilder.DropColumn(
                name: "PrdbDownloadedFromIndexerSyncAttemptedAtUtc",
                table: "DownloadLogs");

            migrationBuilder.DropColumn(
                name: "PrdbDownloadedFromIndexerSyncError",
                table: "DownloadLogs");

            migrationBuilder.DropColumn(
                name: "PrdbDownloadedFromIndexerSyncFingerprint",
                table: "DownloadLogs");

            migrationBuilder.DropColumn(
                name: "PrdbDownloadedFromIndexerSyncedAtUtc",
                table: "DownloadLogs");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "DownloadLogFiles");

            migrationBuilder.DropColumn(
                name: "PrdbDownloadedFromIndexerFilenameId",
                table: "DownloadLogFiles");
        }
    }
}
