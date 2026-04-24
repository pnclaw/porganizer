using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFilehashVideoForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrdbVideoFilehashes_PrdbVideos_VideoId",
                table: "PrdbVideoFilehashes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_PrdbVideoFilehashes_PrdbVideos_VideoId",
                table: "PrdbVideoFilehashes",
                column: "VideoId",
                principalTable: "PrdbVideos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
