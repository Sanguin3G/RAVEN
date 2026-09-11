using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDay5ResearchModesAndArchiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BaseProfileVersionId",
                table: "ResearchRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Initial");

            migrationBuilder.AddColumn<string>(
                name: "ResearchTargetsJson",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ArchivedAt",
                table: "Companies",
                column: "ArchivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_ArchivedAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "BaseProfileVersionId",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "ResearchTargetsJson",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Companies");
        }
    }
}
