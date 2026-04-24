using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDetailSyncedAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DetailSyncedAtUtc",
                table: "PrdbVideos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DetailSyncedAtUtc",
                table: "PrdbActors",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailSyncedAtUtc",
                table: "PrdbVideos");

            migrationBuilder.DropColumn(
                name: "DetailSyncedAtUtc",
                table: "PrdbActors");
        }
    }
}
