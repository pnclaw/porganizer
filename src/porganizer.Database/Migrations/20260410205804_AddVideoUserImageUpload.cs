using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoUserImageUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VideoUserImageUploadEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "VideoUserImageUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrdbVideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrdbVideoUserImageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreviewImageType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoUserImageUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoUserImageUploads_LibraryFiles_LibraryFileId",
                        column: x => x.LibraryFileId,
                        principalTable: "LibraryFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "VideoUserImageUploadEnabled",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_VideoUserImageUploads_LibraryFileId",
                table: "VideoUserImageUploads",
                column: "LibraryFileId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoUserImageUploads_PrdbVideoId",
                table: "VideoUserImageUploads",
                column: "PrdbVideoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoUserImageUploads");

            migrationBuilder.DropColumn(
                name: "VideoUserImageUploadEnabled",
                table: "AppSettings");
        }
    }
}
