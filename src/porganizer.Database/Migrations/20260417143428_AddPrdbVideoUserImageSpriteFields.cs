using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPrdbVideoUserImageSpriteFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SpriteColumns",
                table: "PrdbVideoUserImages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpriteRows",
                table: "PrdbVideoUserImages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpriteTileCount",
                table: "PrdbVideoUserImages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpriteTileHeight",
                table: "PrdbVideoUserImages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpriteTileWidth",
                table: "PrdbVideoUserImages",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpriteColumns",
                table: "PrdbVideoUserImages");

            migrationBuilder.DropColumn(
                name: "SpriteRows",
                table: "PrdbVideoUserImages");

            migrationBuilder.DropColumn(
                name: "SpriteTileCount",
                table: "PrdbVideoUserImages");

            migrationBuilder.DropColumn(
                name: "SpriteTileHeight",
                table: "PrdbVideoUserImages");

            migrationBuilder.DropColumn(
                name: "SpriteTileWidth",
                table: "PrdbVideoUserImages");
        }
    }
}
