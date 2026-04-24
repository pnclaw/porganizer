using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPrdbEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrdbNetworks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbNetworks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrdbSites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NetworkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbSites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrdbSites_PrdbNetworks_NetworkId",
                        column: x => x.NetworkId,
                        principalTable: "PrdbNetworks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PrdbVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrdbCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PrdbUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrdbVideos_PrdbSites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "PrdbSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrdbVideoImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CdnPath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbVideoImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrdbVideoImages_PrdbVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "PrdbVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrdbVideoPreNames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbVideoPreNames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrdbVideoPreNames_PrdbVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "PrdbVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrdbSites_NetworkId",
                table: "PrdbSites",
                column: "NetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_PrdbVideoImages_VideoId",
                table: "PrdbVideoImages",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_PrdbVideoPreNames_VideoId",
                table: "PrdbVideoPreNames",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_PrdbVideos_SiteId",
                table: "PrdbVideos",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrdbVideoImages");

            migrationBuilder.DropTable(
                name: "PrdbVideoPreNames");

            migrationBuilder.DropTable(
                name: "PrdbVideos");

            migrationBuilder.DropTable(
                name: "PrdbSites");

            migrationBuilder.DropTable(
                name: "PrdbNetworks");
        }
    }
}
