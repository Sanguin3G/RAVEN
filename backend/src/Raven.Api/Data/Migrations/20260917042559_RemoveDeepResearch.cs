using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDeepResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeepResearchActivities");

            migrationBuilder.DropTable(
                name: "DeepResearchRuns");

            migrationBuilder.DropColumn(
                name: "DeepResearchRunId",
                table: "SavedResearchArtifacts");

            migrationBuilder.DropColumn(
                name: "DeepResearchModel",
                table: "ResearchSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeepResearchRunId",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeepResearchModel",
                table: "ResearchSettings",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DeepResearchRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CancelRequestedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CrawlCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DocumentsRead = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ResultMarkdown = table.Column<string>(type: "TEXT", maxLength: 40000, nullable: true),
                    SearchCalls = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ToolCalls = table.Column<int>(type: "INTEGER", nullable: false)
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
                name: "DeepResearchActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeepResearchRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 280, nullable: true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceDocumentIdsJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
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
        }
    }
}
