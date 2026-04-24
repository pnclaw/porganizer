using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    LastIndexedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IndexingStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LibraryFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryFolderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    OsHash = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    PHash = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HashComputedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryFiles_LibraryFolders_LibraryFolderId",
                        column: x => x.LibraryFolderId,
                        principalTable: "LibraryFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibraryFiles_PrdbVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "PrdbVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFiles_LibraryFolderId",
                table: "LibraryFiles",
                column: "LibraryFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFiles_OsHash",
                table: "LibraryFiles",
                column: "OsHash");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFiles_VideoId",
                table: "LibraryFiles",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFolders_Path",
                table: "LibraryFolders",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryFiles");

            migrationBuilder.DropTable(
                name: "LibraryFolders");
        }
    }
}
