using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBriefingGenerationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BriefingGenerationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BriefingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RequestJson = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    ResultBriefingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResultVersionNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BriefingGenerationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BriefingGenerationJobs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BriefingGenerationJobs_ResearchBriefings_BriefingId",
                        column: x => x.BriefingId,
                        principalTable: "ResearchBriefings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BriefingGenerationJobs_BriefingId",
                table: "BriefingGenerationJobs",
                column: "BriefingId");

            migrationBuilder.CreateIndex(
                name: "IX_BriefingGenerationJobs_CompanyId_CreatedAt",
                table: "BriefingGenerationJobs",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BriefingGenerationJobs_Status_CreatedAt",
                table: "BriefingGenerationJobs",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BriefingGenerationJobs");
        }
    }
}
