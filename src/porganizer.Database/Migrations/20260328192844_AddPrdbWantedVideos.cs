using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPrdbWantedVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PrdbWantedVideoLastSyncedAt",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrdbWantedVideos",
                columns: table => new
                {
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsFulfilled = table.Column<bool>(type: "INTEGER", nullable: false),
                    FulfilledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FulfilledInQuality = table.Column<int>(type: "INTEGER", nullable: true),
                    FulfillmentExternalId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FulfillmentByApp = table.Column<int>(type: "INTEGER", nullable: true),
                    PrdbCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PrdbUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbWantedVideos", x => x.VideoId);
                    table.ForeignKey(
                        name: "FK_PrdbWantedVideos_PrdbVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "PrdbVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "PrdbWantedVideoLastSyncedAt",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrdbWantedVideos");

            migrationBuilder.DropColumn(
                name: "PrdbWantedVideoLastSyncedAt",
                table: "AppSettings");
        }
    }
}
