using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IndexerRowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DownloadClientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NzbName = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NzbUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ClientItemId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    FileNames = table.Column<string>(type: "TEXT", nullable: true),
                    TotalSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    DownloadedBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LastPolledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadLogs_DownloadClients_DownloadClientId",
                        column: x => x.DownloadClientId,
                        principalTable: "DownloadClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadLogs_IndexerRows_IndexerRowId",
                        column: x => x.IndexerRowId,
                        principalTable: "IndexerRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_DownloadClientId",
                table: "DownloadLogs",
                column: "DownloadClientId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_IndexerRowId",
                table: "DownloadLogs",
                column: "IndexerRowId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadLogs");
        }
    }
}
