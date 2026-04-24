using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexerRowMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IndexerRowMatchLastRunAt",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IndexerRowMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IndexerRowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrdbVideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchedPreNameId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchedTitle = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    MatchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexerRowMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndexerRowMatches_IndexerRows_IndexerRowId",
                        column: x => x.IndexerRowId,
                        principalTable: "IndexerRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IndexerRowMatches_PrdbVideoPreNames_MatchedPreNameId",
                        column: x => x.MatchedPreNameId,
                        principalTable: "PrdbVideoPreNames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IndexerRowMatches_PrdbVideos_PrdbVideoId",
                        column: x => x.PrdbVideoId,
                        principalTable: "PrdbVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "IndexerRowMatchLastRunAt",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_IndexerRowMatches_IndexerRowId",
                table: "IndexerRowMatches",
                column: "IndexerRowId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndexerRowMatches_MatchedPreNameId",
                table: "IndexerRowMatches",
                column: "MatchedPreNameId");

            migrationBuilder.CreateIndex(
                name: "IX_IndexerRowMatches_PrdbVideoId",
                table: "IndexerRowMatches",
                column: "PrdbVideoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndexerRowMatches");

            migrationBuilder.DropColumn(
                name: "IndexerRowMatchLastRunAt",
                table: "AppSettings");
        }
    }
}
