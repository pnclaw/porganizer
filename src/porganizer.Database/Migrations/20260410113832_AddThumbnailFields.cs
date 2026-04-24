using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddThumbnailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SpriteSheetGeneratedAtUtc",
                table: "LibraryFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpriteSheetTileCount",
                table: "LibraryFiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FfmpegPath",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailCachePath",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "FfmpegPath", "ThumbnailCachePath" },
                values: new object[] { "ffmpeg", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpriteSheetGeneratedAtUtc",
                table: "LibraryFiles");

            migrationBuilder.DropColumn(
                name: "SpriteSheetTileCount",
                table: "LibraryFiles");

            migrationBuilder.DropColumn(
                name: "FfmpegPath",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ThumbnailCachePath",
                table: "AppSettings");
        }
    }
}
