using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceResearchReviewState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceResearchReviewStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReviewKey = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TopicKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AcknowledgedThrough = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceResearchReviewStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceResearchReviewStates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceResearchReviewStates_CompanyId_AcknowledgedThrough",
                table: "WorkspaceResearchReviewStates",
                columns: new[] { "CompanyId", "AcknowledgedThrough" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceResearchReviewStates_ReviewKey",
                table: "WorkspaceResearchReviewStates",
                column: "ReviewKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceResearchReviewStates");
        }
    }
}
