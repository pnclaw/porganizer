using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPrdbVideoFilehashes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrdbFilehashBackfillPage",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrdbFilehashBackfillTotalCount",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrdbFilehashSyncCursorUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrdbVideoFilehashes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Filename = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    OsHash = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    PHash = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Filesize = table.Column<long>(type: "INTEGER", nullable: false),
                    SubmissionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrdbCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PrdbUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbVideoFilehashes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrdbVideoFilehashes_PrdbVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "PrdbVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PrdbFilehashBackfillPage", "PrdbFilehashBackfillTotalCount", "PrdbFilehashSyncCursorUtc" },
                values: new object[] { 1, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_PrdbVideoFilehashes_PrdbCreatedAtUtc",
                table: "PrdbVideoFilehashes",
                column: "PrdbCreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PrdbVideoFilehashes_VideoId",
                table: "PrdbVideoFilehashes",
                column: "VideoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrdbVideoFilehashes");

            migrationBuilder.DropColumn(
                name: "PrdbFilehashBackfillPage",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PrdbFilehashBackfillTotalCount",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PrdbFilehashSyncCursorUtc",
                table: "AppSettings");
        }
    }
}
