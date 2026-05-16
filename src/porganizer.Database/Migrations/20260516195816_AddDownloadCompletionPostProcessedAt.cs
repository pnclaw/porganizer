using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadCompletionPostProcessedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletionPostProcessedAtUtc",
                table: "DownloadLogs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionPostProcessedAtUtc",
                table: "DownloadLogs");
        }
    }
}
