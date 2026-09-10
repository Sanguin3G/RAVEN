using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileCandidateReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyProfileCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AiProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AiModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PromptTemplateVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CandidateJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfileCandidates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfileCandidates_ResearchRunId_GeneratedAt",
                table: "CompanyProfileCandidates",
                columns: new[] { "ResearchRunId", "GeneratedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyProfileCandidates");
        }
    }
}
