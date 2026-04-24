using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPrdbFavoriteChangeCursors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PrdbFavoriteActorSyncCursorId",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrdbFavoriteActorSyncCursorUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrdbFavoriteSiteSyncCursorId",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrdbFavoriteSiteSyncCursorUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PrdbFavoriteActorSyncCursorId", "PrdbFavoriteActorSyncCursorUtc", "PrdbFavoriteSiteSyncCursorId", "PrdbFavoriteSiteSyncCursorUtc" },
                values: new object[] { null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrdbFavoriteActorSyncCursorId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PrdbFavoriteActorSyncCursorUtc",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PrdbFavoriteSiteSyncCursorId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PrdbFavoriteSiteSyncCursorUtc",
                table: "AppSettings");
        }
    }
}
