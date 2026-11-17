using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotaDraft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHeroBaselineWinRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BaselineWinRate",
                table: "Heroes",
                type: "double precision",
                nullable: false,
                defaultValue: 0.5); // Hero.DefaultBaselineWinRate, for heroes without games

            // Fill the baselines from the matchups already stored, so counters stay right without waiting for the
            // next seed. The same calculation as the seed: all the hero's wins divided by all its games
            migrationBuilder.Sql("""
                UPDATE "Heroes" h
                SET "BaselineWinRate" = t.wins::double precision / t.games
                FROM (
                    SELECT "HeroId", sum("Wins") AS wins, sum("GamesPlayed") AS games
                    FROM "HeroMatchups"
                    GROUP BY "HeroId"
                ) t
                WHERE t."HeroId" = h."Id" AND t.games > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaselineWinRate",
                table: "Heroes");
        }
    }
}
