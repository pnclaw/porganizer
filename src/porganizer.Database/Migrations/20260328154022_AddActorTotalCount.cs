using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddActorTotalCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrdbActorTotalCount",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "PrdbActorTotalCount",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrdbActorTotalCount",
                table: "AppSettings");
        }
    }
}
