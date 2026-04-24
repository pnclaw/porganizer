using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPrdbPreDbEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrdbPreDbEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PrdbVideoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PrdbSiteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VideoTitle = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SiteTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbPreDbEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrdbPreDbEntries_PrdbSites_PrdbSiteId",
                        column: x => x.PrdbSiteId,
                        principalTable: "PrdbSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PrdbPreDbEntries_PrdbVideos_PrdbVideoId",
                        column: x => x.PrdbVideoId,
                        principalTable: "PrdbVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrdbPreDbEntries_CreatedAtUtc",
                table: "PrdbPreDbEntries",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PrdbPreDbEntries_PrdbSiteId",
                table: "PrdbPreDbEntries",
                column: "PrdbSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_PrdbPreDbEntries_PrdbVideoId",
                table: "PrdbPreDbEntries",
                column: "PrdbVideoId");

            migrationBuilder.CreateIndex(
                name: "IX_PrdbPreDbEntries_Title",
                table: "PrdbPreDbEntries",
                column: "Title");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrdbPreDbEntries");
        }
    }
}
