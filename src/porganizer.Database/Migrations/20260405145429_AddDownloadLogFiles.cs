using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadLogFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileNames",
                table: "DownloadLogs");

            migrationBuilder.CreateTable(
                name: "DownloadLogFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DownloadLogId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    OsHash = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    PHash = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadLogFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadLogFiles_DownloadLogs_DownloadLogId",
                        column: x => x.DownloadLogId,
                        principalTable: "DownloadLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogFiles_DownloadLogId",
                table: "DownloadLogFiles",
                column: "DownloadLogId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadLogFiles");

            migrationBuilder.AddColumn<string>(
                name: "FileNames",
                table: "DownloadLogs",
                type: "TEXT",
                nullable: true);
        }
    }
}
