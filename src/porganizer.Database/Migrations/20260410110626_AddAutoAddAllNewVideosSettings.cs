using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoAddAllNewVideosSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoAddAllNewVideos",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AutoAddAllNewVideosDaysBack",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutoAddAllNewVideosLastRunAt",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AutoAddAllNewVideos", "AutoAddAllNewVideosDaysBack", "AutoAddAllNewVideosLastRunAt" },
                values: new object[] { false, 2, (DateTime?)null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoAddAllNewVideos",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "AutoAddAllNewVideosDaysBack",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "AutoAddAllNewVideosLastRunAt",
                table: "AppSettings");
        }
    }
}
