using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoritesWantedSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FavoritesWantedDaysBack",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "FavoritesWantedEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FavoritesWantedLastRunAt",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "FavoritesWantedDaysBack", "FavoritesWantedEnabled", "FavoritesWantedLastRunAt" },
                values: new object[] { 7, false, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FavoritesWantedDaysBack",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "FavoritesWantedEnabled",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "FavoritesWantedLastRunAt",
                table: "AppSettings");
        }
    }
}
