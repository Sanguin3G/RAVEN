using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Operation",
                table: "ResearchEvents",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<int>(
                name: "ThinkingTokens",
                table: "ResearchEvents",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Operation",
                table: "ResearchEvents");

            migrationBuilder.DropColumn(
                name: "ThinkingTokens",
                table: "ResearchEvents");
        }
    }
}
