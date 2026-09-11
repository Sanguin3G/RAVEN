using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchIntelligenceTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroundingMode",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Auto");

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedIdentityCandidateId",
                table: "ResearchRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProfileChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OldProfileVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NewProfileVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FieldPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ChangeType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OldValueJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
                    NewValueJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
                    DetectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResearchIdentityCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemporaryId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    LegalName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    OfficialDomain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RelationshipHint = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Confidence = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SupportingCandidateIdsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Recommended = table.Column<bool>(type: "INTEGER", nullable: false),
                    Selected = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchIdentityCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchIdentityCandidates_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResearchSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GroundingMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProfileModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GroundingModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DeepResearchModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AiSourceRerankingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProviderPreset = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SearchProviderPriority = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CrawlerProviderPriority = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileChanges_CompanyId_DetectedAt",
                table: "ProfileChanges",
                columns: new[] { "CompanyId", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileChanges_CompanyId_NewProfileVersionId",
                table: "ProfileChanges",
                columns: new[] { "CompanyId", "NewProfileVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchIdentityCandidates_ResearchRunId_TemporaryId",
                table: "ResearchIdentityCandidates",
                columns: new[] { "ResearchRunId", "TemporaryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileChanges");

            migrationBuilder.DropTable(
                name: "ResearchIdentityCandidates");

            migrationBuilder.DropTable(
                name: "ResearchSettings");

            migrationBuilder.DropColumn(
                name: "GroundingMode",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "ResolvedIdentityCandidateId",
                table: "ResearchRuns");
        }
    }
}
