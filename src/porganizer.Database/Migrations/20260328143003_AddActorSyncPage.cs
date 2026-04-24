using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddActorSyncPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrdbActorSyncPage",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "PrdbActorSyncPage",
                value: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrdbActorSyncPage",
                table: "AppSettings");
        }
    }
}
