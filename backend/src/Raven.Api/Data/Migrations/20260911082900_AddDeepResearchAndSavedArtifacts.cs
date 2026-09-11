using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeepResearchAndSavedArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeepResearchRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Question = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancelRequestedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ToolCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    SearchCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    CrawlCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentsRead = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultMarkdown = table.Column<string>(type: "TEXT", maxLength: 40000, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeepResearchRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeepResearchRuns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SavedResearchArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeepResearchRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ResearchType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SourceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceDocumentIdsJson = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedResearchArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedResearchArtifacts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeepResearchActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeepResearchRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 280, nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SourceDocumentIdsJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeepResearchActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeepResearchActivities_DeepResearchRuns_DeepResearchRunId",
                        column: x => x.DeepResearchRunId,
                        principalTable: "DeepResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeepResearchActivities_DeepResearchRunId_Sequence",
                table: "DeepResearchActivities",
                columns: new[] { "DeepResearchRunId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeepResearchRuns_CompanyId_CreatedAt",
                table: "DeepResearchRuns",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedResearchArtifacts_CompanyId_CreatedAt",
                table: "SavedResearchArtifacts",
                columns: new[] { "CompanyId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeepResearchActivities");

            migrationBuilder.DropTable(
                name: "SavedResearchArtifacts");

            migrationBuilder.DropTable(
                name: "DeepResearchRuns");
        }
    }
}
