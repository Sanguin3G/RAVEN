using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvestigationLifecycleAndBriefings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "GeneralResearch");

            migrationBuilder.AddColumn<string>(
                name: "TopicsJson",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceInvestigationId",
                table: "ResearchRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceInvestigationKind",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SourceInvestigationUpdatedAt",
                table: "ResearchRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvestigationReviewStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MaterialKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MaterialId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DoneThrough = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReopenedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AppliedProfileVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationReviewStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationReviewStates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestigationReviewStates_CompanyProfileVersions_AppliedProfileVersionId",
                        column: x => x.AppliedProfileVersionId,
                        principalTable: "CompanyProfileVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResearchBriefings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Template = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Objective = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchBriefings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchBriefings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResearchBriefingVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BriefingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ResearchThrough = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Template = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Objective = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SectionsJson = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: false),
                    SourcesJson = table.Column<string>(type: "TEXT", maxLength: 600000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchBriefingVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchBriefingVersions_ResearchBriefings_BriefingId",
                        column: x => x.BriefingId,
                        principalTable: "ResearchBriefings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationReviewStates_AppliedProfileVersionId",
                table: "InvestigationReviewStates",
                column: "AppliedProfileVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationReviewStates_CompanyId_MaterialKind_MaterialId",
                table: "InvestigationReviewStates",
                columns: new[] { "CompanyId", "MaterialKind", "MaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchBriefings_CompanyId_UpdatedAt",
                table: "ResearchBriefings",
                columns: new[] { "CompanyId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchBriefingVersions_BriefingId_VersionNumber",
                table: "ResearchBriefingVersions",
                columns: new[] { "BriefingId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestigationReviewStates");

            migrationBuilder.DropTable(
                name: "ResearchBriefingVersions");

            migrationBuilder.DropTable(
                name: "ResearchBriefings");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "SavedResearchArtifacts");

            migrationBuilder.DropColumn(
                name: "TopicsJson",
                table: "SavedResearchArtifacts");

            migrationBuilder.DropColumn(
                name: "SourceInvestigationId",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "SourceInvestigationKind",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "SourceInvestigationUpdatedAt",
                table: "ResearchRuns");
        }
    }
}
