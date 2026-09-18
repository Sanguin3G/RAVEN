using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ManagedResearchProductSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManagedResearchDepth",
                table: "ResearchSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Adaptive");

            migrationBuilder.AddColumn<string>(
                name: "ManagedResearchProvider",
                table: "ResearchSettings",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "exa-agent");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManagedResearchDepth",
                table: "ResearchSettings");

            migrationBuilder.DropColumn(
                name: "ManagedResearchProvider",
                table: "ResearchSettings");
        }
    }
}
