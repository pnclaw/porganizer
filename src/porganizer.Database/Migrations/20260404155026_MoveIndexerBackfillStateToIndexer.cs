using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class MoveIndexerBackfillStateToIndexer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BackfillCompletedAtUtc",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BackfillCurrentOffset",
                table: "Indexers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BackfillCutoffUtc",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BackfillDays",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<DateTime>(
                name: "BackfillLastRunAtUtc",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BackfillStartedAtUtc",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Indexers"
                SET "BackfillDays" = COALESCE((SELECT "IndexerBackfillDays" FROM "AppSettings" WHERE "Id" = 1), 30),
                    "BackfillStartedAtUtc" = (SELECT "IndexerBackfillStartedAtUtc" FROM "AppSettings" WHERE "Id" = 1),
                    "BackfillCutoffUtc" = (SELECT "IndexerBackfillCutoffUtc" FROM "AppSettings" WHERE "Id" = 1),
                    "BackfillCompletedAtUtc" = (SELECT "IndexerBackfillCompletedAtUtc" FROM "AppSettings" WHERE "Id" = 1),
                    "BackfillLastRunAtUtc" = (SELECT "IndexerBackfillLastRunAtUtc" FROM "AppSettings" WHERE "Id" = 1);
                """);

            migrationBuilder.Sql("""
                UPDATE "Indexers"
                SET "BackfillCurrentOffset" = (
                    SELECT "IndexerBackfillCurrentOffset"
                    FROM "AppSettings"
                    WHERE "Id" = 1
                )
                WHERE "Id" = (
                    SELECT "IndexerBackfillCurrentIndexerId"
                    FROM "AppSettings"
                    WHERE "Id" = 1
                );
                """);

            migrationBuilder.DropColumn(
                name: "IndexerBackfillCompletedAtUtc",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillCurrentIndexerId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillCurrentOffset",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillCutoffUtc",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillDays",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillLastRunAtUtc",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "IndexerBackfillStartedAtUtc",
                table: "AppSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackfillCompletedAtUtc",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "BackfillCurrentOffset",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "BackfillCutoffUtc",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "BackfillDays",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "BackfillLastRunAtUtc",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "BackfillStartedAtUtc",
                table: "Indexers");

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexerBackfillCompletedAtUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IndexerBackfillCurrentIndexerId",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IndexerBackfillCurrentOffset",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexerBackfillCutoffUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IndexerBackfillDays",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexerBackfillLastRunAtUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexerBackfillStartedAtUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "AppSettings"
                SET "IndexerBackfillDays" = COALESCE((
                        SELECT "BackfillDays"
                        FROM "Indexers"
                        ORDER BY "CreatedAt", "Title"
                        LIMIT 1
                    ), 30),
                    "IndexerBackfillStartedAtUtc" = (
                        SELECT "BackfillStartedAtUtc"
                        FROM "Indexers"
                        WHERE "BackfillStartedAtUtc" IS NOT NULL
                        ORDER BY "CreatedAt", "Title"
                        LIMIT 1
                    ),
                    "IndexerBackfillCutoffUtc" = (
                        SELECT "BackfillCutoffUtc"
                        FROM "Indexers"
                        WHERE "BackfillCutoffUtc" IS NOT NULL
                        ORDER BY "CreatedAt", "Title"
                        LIMIT 1
                    ),
                    "IndexerBackfillCompletedAtUtc" = (
                        SELECT "BackfillCompletedAtUtc"
                        FROM "Indexers"
                        WHERE "BackfillCompletedAtUtc" IS NOT NULL
                        ORDER BY "CreatedAt", "Title"
                        LIMIT 1
                    ),
                    "IndexerBackfillLastRunAtUtc" = (
                        SELECT "BackfillLastRunAtUtc"
                        FROM "Indexers"
                        WHERE "BackfillLastRunAtUtc" IS NOT NULL
                        ORDER BY "CreatedAt", "Title"
                        LIMIT 1
                    ),
                    "IndexerBackfillCurrentIndexerId" = (
                        SELECT "Id"
                        FROM "Indexers"
                        WHERE "BackfillCurrentOffset" IS NOT NULL
                        ORDER BY "CreatedAt", "Title"
                        LIMIT 1
                    ),
                    "IndexerBackfillCurrentOffset" = (
                        SELECT "BackfillCurrentOffset"
                        FROM "Indexers"
                        WHERE "BackfillCurrentOffset" IS NOT NULL
                        ORDER BY "CreatedAt", "Title"
                        LIMIT 1
                    )
                WHERE "Id" = 1;
                """);
        }
    }
}
