using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStagedResearchProfilesAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IconUrl",
                table: "SourceDocuments",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceKind",
                table: "SourceDocuments",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "ExternalWebsite");

            migrationBuilder.AddColumn<string>(
                name: "StructuredFactsJson",
                table: "SourceDocuments",
                type: "TEXT",
                maxLength: 16000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CrawlCompleted",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CrawlFailed",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CrawlSucceeded",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CrawlTotal",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DocumentsAdded",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DuplicatesSkipped",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QueriesCompleted",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QueriesTotal",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RecommendedCandidates",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResearchHint",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 48,
                nullable: false,
                defaultValue: "Completed");

            migrationBuilder.Sql("UPDATE ResearchRuns SET Stage = CASE Status WHEN 'Failed' THEN 'Failed' WHEN 'Searching' THEN 'Discovering' WHEN 'Crawling' THEN 'Acquiring' ELSE 'Completed' END;");

            migrationBuilder.AddColumn<int>(
                name: "UniqueCandidates",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CompanyProfileVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AiProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AiModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PromptTemplateVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ProfileJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfileVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfileEvidences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyProfileCandidateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompanyProfileVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FieldPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SourceDocumentIdsJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileEvidences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResearchCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    NormalizedUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Snippet = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SearchRank = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    RecommendationReasonsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Recommended = table.Column<bool>(type: "INTEGER", nullable: false),
                    Selected = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcquisitionStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AcquisitionError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IconUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchCandidates_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResearchEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 48, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ToolName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    InputSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    OutputSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    HttpStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    ExternalRequestId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    CachedTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    PromptTemplateVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    InputHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfileVersions_CompanyId_ConfirmedAt",
                table: "CompanyProfileVersions",
                columns: new[] { "CompanyId", "ConfirmedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfileVersions_CompanyId_Version",
                table: "CompanyProfileVersions",
                columns: new[] { "CompanyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileEvidences_CompanyProfileVersionId_FieldPath",
                table: "ProfileEvidences",
                columns: new[] { "CompanyProfileVersionId", "FieldPath" });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchCandidates_ResearchRunId_NormalizedUrl",
                table: "ResearchCandidates",
                columns: new[] { "ResearchRunId", "NormalizedUrl" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchEvents_ResearchRunId_Sequence",
                table: "ResearchEvents",
                columns: new[] { "ResearchRunId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyProfileVersions");

            migrationBuilder.DropTable(
                name: "ProfileEvidences");

            migrationBuilder.DropTable(
                name: "ResearchCandidates");

            migrationBuilder.DropTable(
                name: "ResearchEvents");

            migrationBuilder.DropColumn(
                name: "IconUrl",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "SourceKind",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "StructuredFactsJson",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "CrawlCompleted",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "CrawlFailed",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "CrawlSucceeded",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "CrawlTotal",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "DocumentsAdded",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "DuplicatesSkipped",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "QueriesCompleted",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "QueriesTotal",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "RecommendedCandidates",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "ResearchHint",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "UniqueCandidates",
                table: "ResearchRuns");
        }
    }
}
