using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManagedResearchInvestigations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChatMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Objective = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedResearchInvestigations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManagedResearchInvestigations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManagedResearchJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChatMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Objective = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ProviderQuery = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    Effort = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ProviderRunId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ProviderRunStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CancelRequestedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ProviderCostDollars = table.Column<decimal>(type: "TEXT", nullable: true),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedResearchJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManagedResearchJobs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedResearchInvestigations_CompanyId_CompletedAt",
                table: "ManagedResearchInvestigations",
                columns: new[] { "CompanyId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedResearchInvestigations_JobId",
                table: "ManagedResearchInvestigations",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedResearchJobs_CompanyId_CreatedAt",
                table: "ManagedResearchJobs",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedResearchJobs_Status_CreatedAt",
                table: "ManagedResearchJobs",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManagedResearchInvestigations");

            migrationBuilder.DropTable(
                name: "ManagedResearchJobs");
        }
    }
}
