using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResearchRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RequestedSearchProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActualSearchProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RequestedCrawlerProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActualCrawlerProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SourcesFound = table.Column<int>(type: "INTEGER", nullable: false),
                    SourcesSelected = table.Column<int>(type: "INTEGER", nullable: false),
                    SourcesCrawled = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchRuns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    NormalizedUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SourceDomain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CrawlerProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceDocuments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceDocuments_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchRuns_CompanyId_StartedAt",
                table: "ResearchRuns",
                columns: new[] { "CompanyId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_CompanyId_ContentHash",
                table: "SourceDocuments",
                columns: new[] { "CompanyId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_CompanyId_NormalizedUrl",
                table: "SourceDocuments",
                columns: new[] { "CompanyId", "NormalizedUrl" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_ResearchRunId",
                table: "SourceDocuments",
                column: "ResearchRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceDocuments");

            migrationBuilder.DropTable(
                name: "ResearchRuns");
        }
    }
}
