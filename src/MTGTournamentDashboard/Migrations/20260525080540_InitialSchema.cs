using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MTGTournamentDashboard.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Handle = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tournaments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlayerCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IncludeInWinrate = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tournaments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Decks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TournamentId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Archetype = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ArchetypeRulesVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false),
                    Draws = table.Column<int>(type: "INTEGER", nullable: false),
                    FinalRank = table.Column<int>(type: "INTEGER", nullable: true),
                    MainboardJson = table.Column<string>(type: "TEXT", nullable: false),
                    SideboardJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Decks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Decks_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Decks_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TournamentId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoundNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    DeckAId = table.Column<int>(type: "INTEGER", nullable: false),
                    DeckBId = table.Column<int>(type: "INTEGER", nullable: false),
                    WinnerDeckId = table.Column<int>(type: "INTEGER", nullable: true),
                    GamesA = table.Column<int>(type: "INTEGER", nullable: false),
                    GamesB = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rounds_Decks_DeckAId",
                        column: x => x.DeckAId,
                        principalTable: "Decks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rounds_Decks_DeckBId",
                        column: x => x.DeckBId,
                        principalTable: "Decks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rounds_Decks_WinnerDeckId",
                        column: x => x.WinnerDeckId,
                        principalTable: "Decks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rounds_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Decks_Archetype",
                table: "Decks",
                column: "Archetype");

            migrationBuilder.CreateIndex(
                name: "IX_Decks_PlayerId",
                table: "Decks",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Decks_TournamentId_PlayerId",
                table: "Decks",
                columns: new[] { "TournamentId", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_Name_Handle",
                table: "Players",
                columns: new[] { "Name", "Handle" });

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_DeckAId",
                table: "Rounds",
                column: "DeckAId");

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_DeckBId",
                table: "Rounds",
                column: "DeckBId");

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_TournamentId_RoundNumber",
                table: "Rounds",
                columns: new[] { "TournamentId", "RoundNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_WinnerDeckId",
                table: "Rounds",
                column: "WinnerDeckId");

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_Format_Date",
                table: "Tournaments",
                columns: new[] { "Format", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_Source_SourceUrl",
                table: "Tournaments",
                columns: new[] { "Source", "SourceUrl" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Rounds");

            migrationBuilder.DropTable(
                name: "Decks");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Tournaments");
        }
    }
}
