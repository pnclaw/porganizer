using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoUserImageUploadCompletionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "VideoUserImageUploadCompletedAtUtc",
                table: "LibraryFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoUserImageUploadCompletionReason",
                table: "LibraryFiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoUserImageUploadRemoteImageCount",
                table: "LibraryFiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "LibraryFiles"
                SET
                    "VideoUserImageUploadCompletedAtUtc" = (
                        SELECT MAX("UploadedAtUtc")
                        FROM "VideoUserImageUploads"
                        WHERE "VideoUserImageUploads"."LibraryFileId" = "LibraryFiles"."Id"
                    ),
                    "VideoUserImageUploadCompletionReason" = 1,
                    "VideoUserImageUploadRemoteImageCount" = (
                        SELECT COUNT(*)
                        FROM "VideoUserImageUploads"
                        WHERE "VideoUserImageUploads"."LibraryFileId" = "LibraryFiles"."Id"
                    )
                WHERE "PreviewImageCount" IS NOT NULL
                  AND (
                      SELECT COUNT(*)
                      FROM "VideoUserImageUploads"
                      WHERE "VideoUserImageUploads"."LibraryFileId" = "LibraryFiles"."Id"
                        AND "VideoUserImageUploads"."PreviewImageType" = 'Single'
                  ) = "PreviewImageCount"
                  AND (
                      SELECT COUNT(*)
                      FROM "VideoUserImageUploads"
                      WHERE "VideoUserImageUploads"."LibraryFileId" = "LibraryFiles"."Id"
                        AND "VideoUserImageUploads"."PreviewImageType" = 'SpriteSheet'
                  ) = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoUserImageUploadCompletedAtUtc",
                table: "LibraryFiles");

            migrationBuilder.DropColumn(
                name: "VideoUserImageUploadCompletionReason",
                table: "LibraryFiles");

            migrationBuilder.DropColumn(
                name: "VideoUserImageUploadRemoteImageCount",
                table: "LibraryFiles");
        }
    }
}
