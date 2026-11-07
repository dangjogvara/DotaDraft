using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotaDraft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSeedingState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeedingStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MatchupsSeededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedingStates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeedingStates");
        }
    }
}
