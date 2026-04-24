using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviewImageSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreviewImageCount",
                table: "LibraryFiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreviewImagesGeneratedAtUtc",
                table: "LibraryFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PreviewImageGenerationEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PreviewImageGenerationMatchedOnly",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PreviewImageGenerationEnabled", "PreviewImageGenerationMatchedOnly" },
                values: new object[] { false, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviewImageCount",
                table: "LibraryFiles");

            migrationBuilder.DropColumn(
                name: "PreviewImagesGeneratedAtUtc",
                table: "LibraryFiles");

            migrationBuilder.DropColumn(
                name: "PreviewImageGenerationEnabled",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PreviewImageGenerationMatchedOnly",
                table: "AppSettings");
        }
    }
}
