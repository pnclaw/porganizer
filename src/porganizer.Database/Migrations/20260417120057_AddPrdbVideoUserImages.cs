using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPrdbVideoUserImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PrdbVideoUserImageSyncCursorId",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrdbVideoUserImageSyncCursorUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrdbVideoUserImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    PreviewImageType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ModerationVisibility = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PrdbUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbVideoUserImages", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PrdbVideoUserImageSyncCursorId", "PrdbVideoUserImageSyncCursorUtc" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "IX_PrdbVideoUserImages_PrdbUpdatedAtUtc",
                table: "PrdbVideoUserImages",
                column: "PrdbUpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PrdbVideoUserImages_VideoId",
                table: "PrdbVideoUserImages",
                column: "VideoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrdbVideoUserImages");

            migrationBuilder.DropColumn(
                name: "PrdbVideoUserImageSyncCursorId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PrdbVideoUserImageSyncCursorUtc",
                table: "AppSettings");
        }
    }
}
