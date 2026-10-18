using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotaDraft.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameMatchupAdvantageToWinRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Advantage",
                table: "HeroMatchups",
                newName: "WinRate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WinRate",
                table: "HeroMatchups",
                newName: "Advantage");
        }
    }
}
