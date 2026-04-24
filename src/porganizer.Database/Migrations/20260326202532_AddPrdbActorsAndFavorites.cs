using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace porganizer.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPrdbActorsAndFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FavoritedAtUtc",
                table: "PrdbSites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "PrdbSites",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PrdbActors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Gender = table.Column<int>(type: "INTEGER", nullable: false),
                    Birthday = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    BirthdayType = table.Column<int>(type: "INTEGER", nullable: true),
                    Deathday = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Birthplace = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Haircolor = table.Column<int>(type: "INTEGER", nullable: false),
                    Eyecolor = table.Column<int>(type: "INTEGER", nullable: false),
                    BreastType = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    BraSize = table.Column<int>(type: "INTEGER", nullable: true),
                    BraSizeLabel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    WaistSize = table.Column<int>(type: "INTEGER", nullable: true),
                    HipSize = table.Column<int>(type: "INTEGER", nullable: true),
                    Nationality = table.Column<int>(type: "INTEGER", nullable: false),
                    Ethnicity = table.Column<int>(type: "INTEGER", nullable: false),
                    CareerStart = table.Column<int>(type: "INTEGER", nullable: true),
                    CareerEnd = table.Column<int>(type: "INTEGER", nullable: true),
                    Tattoos = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Piercings = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PrdbCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PrdbUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbActors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrdbActorAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbActorAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrdbActorAliases_PrdbActors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "PrdbActors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrdbActorImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImageType = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbActorImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrdbActorImages_PrdbActors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "PrdbActors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrdbVideoActors",
                columns: table => new
                {
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdbVideoActors", x => new { x.VideoId, x.ActorId });
                    table.ForeignKey(
                        name: "FK_PrdbVideoActors_PrdbActors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "PrdbActors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrdbVideoActors_PrdbVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "PrdbVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrdbActorAliases_ActorId",
                table: "PrdbActorAliases",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_PrdbActorImages_ActorId",
                table: "PrdbActorImages",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_PrdbVideoActors_ActorId",
                table: "PrdbVideoActors",
                column: "ActorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrdbActorAliases");

            migrationBuilder.DropTable(
                name: "PrdbActorImages");

            migrationBuilder.DropTable(
                name: "PrdbVideoActors");

            migrationBuilder.DropTable(
                name: "PrdbActors");

            migrationBuilder.DropColumn(
                name: "FavoritedAtUtc",
                table: "PrdbSites");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "PrdbSites");
        }
    }
}
