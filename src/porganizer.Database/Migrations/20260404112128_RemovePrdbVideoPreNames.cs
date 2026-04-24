using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemovePrdbVideoPreNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IndexerRowMatches_PrdbVideoPreNames_MatchedPreNameId",
                table: "IndexerRowMatches");

            migrationBuilder.DropTable(
                name: "PrdbVideoPreNames");

            migrationBuilder.RenameColumn(
                name: "MatchedPreNameId",
                table: "IndexerRowMatches",
                newName: "MatchedPreDbEntryId");

            migrationBuilder.RenameIndex(
                name: "IX_IndexerRowMatches_MatchedPreNameId",
                table: "IndexerRowMatches",
                newName: "IX_IndexerRowMatches_MatchedPreDbEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_IndexerRowMatches_PrdbPreDbEntries_MatchedPreDbEntryId",
                table: "IndexerRowMatches",
                column: "MatchedPreDbEntryId",
                principalTable: "PrdbPreDbEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IndexerRowMatches_PrdbPreDbEntries_MatchedPreDbEntryId",
                table: "IndexerRowMatches");

            migrationBuilder.RenameColumn(
                name: "MatchedPreDbEntryId",
                table: "IndexerRowMatches",
                newName: "MatchedPreNameId");

            migrationBuilder.RenameIndex(
                name: "IX_IndexerRowMatches_MatchedPreDbEntryId",
                table: "IndexerRowMatches",
                newName: "IX_IndexerRowMatches_MatchedPreNameId");

            migrationBuilder.CreateTable(
                name: "PrdbVideoPreNames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbVideoPreNames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrdbVideoPreNames_PrdbVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "PrdbVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrdbVideoPreNames_VideoId",
                table: "PrdbVideoPreNames",
                column: "VideoId");

            migrationBuilder.AddForeignKey(
                name: "FK_IndexerRowMatches_PrdbVideoPreNames_MatchedPreNameId",
                table: "IndexerRowMatches",
                column: "MatchedPreNameId",
                principalTable: "PrdbVideoPreNames",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
