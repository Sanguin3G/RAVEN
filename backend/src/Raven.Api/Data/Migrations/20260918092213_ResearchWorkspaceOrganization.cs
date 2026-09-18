using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ResearchWorkspaceOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RawResponse",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                maxLength: 200000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvestigationOrganizationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SavedResearchArtifactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExecutiveSummary = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    ThemesJson = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: false),
                    EvidenceGapsJson = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: false),
                    SuggestedFollowUpsJson = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: false),
                    UncertaintiesJson = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: false),
                    IsHumanEdited = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationOrganizationRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationOrganizationRevisions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestigationOrganizationRevisions_SavedResearchArtifacts_SavedResearchArtifactId",
                        column: x => x.SavedResearchArtifactId,
                        principalTable: "SavedResearchArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationOrganizationRevisions_CompanyId",
                table: "InvestigationOrganizationRevisions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationOrganizationRevisions_SavedResearchArtifactId_Version",
                table: "InvestigationOrganizationRevisions",
                columns: new[] { "SavedResearchArtifactId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestigationOrganizationRevisions");

            migrationBuilder.DropColumn(
                name: "RawResponse",
                table: "SavedResearchArtifacts");
        }
    }
}
