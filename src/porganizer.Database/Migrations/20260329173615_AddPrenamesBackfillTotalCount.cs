using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPrenamesBackfillTotalCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrenamesBackfillTotalCount",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "PrenamesBackfillTotalCount",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrenamesBackfillTotalCount",
                table: "AppSettings");
        }
    }
}
