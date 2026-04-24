using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddFileMoveSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FilesMovedAtUtc",
                table: "DownloadLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletedDownloadsTargetFolder",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OrganizeCompletedBySite",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RenameCompletedFiles",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CompletedDownloadsTargetFolder", "OrganizeCompletedBySite", "RenameCompletedFiles" },
                values: new object[] { null, false, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilesMovedAtUtc",
                table: "DownloadLogs");

            migrationBuilder.DropColumn(
                name: "CompletedDownloadsTargetFolder",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "OrganizeCompletedBySite",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RenameCompletedFiles",
                table: "AppSettings");
        }
    }
}
