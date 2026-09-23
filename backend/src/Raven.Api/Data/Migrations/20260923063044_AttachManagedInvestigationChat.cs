using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AttachManagedInvestigationChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResearchContextAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttachedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchContextAttachments", item => item.Id);
                    table.ForeignKey("FK_ResearchContextAttachments_Companies_CompanyId", item => item.CompanyId,
                        "Companies", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_ResearchContextAttachments_ManagedResearchInvestigations_InvestigationId", item => item.InvestigationId,
                        "ManagedResearchInvestigations", "Id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("IX_ResearchContextAttachments_CompanyId_ConversationId_InvestigationId",
                "ResearchContextAttachments", new[] { "CompanyId", "ConversationId", "InvestigationId" }, unique: true);
            migrationBuilder.CreateIndex("IX_ResearchContextAttachments_InvestigationId",
                "ResearchContextAttachments", "InvestigationId");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations");

            migrationBuilder.AddColumn<bool>(
                name: "AnswerInChat",
                table: "ManagedResearchJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ManagedResearchJobId",
                table: "ChatMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvestigationId",
                table: "ChatCitations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ManagedResearchJobId",
                table: "ChatMessages",
                column: "ManagedResearchJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatCitations_ChatMessageId_InvestigationId",
                table: "ChatCitations",
                columns: new[] { "ChatMessageId", "InvestigationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatCitations_InvestigationId",
                table: "ChatCitations",
                column: "InvestigationId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations",
                sql: "(\"SourceDocumentId\" IS NOT NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NOT NULL AND \"InvestigationId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatCitations_ManagedResearchInvestigations_InvestigationId",
                table: "ChatCitations",
                column: "InvestigationId",
                principalTable: "ManagedResearchInvestigations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ResearchContextAttachments");
            migrationBuilder.DropForeignKey(
                name: "FK_ChatCitations_ManagedResearchInvestigations_InvestigationId",
                table: "ChatCitations");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ManagedResearchJobId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatCitations_ChatMessageId_InvestigationId",
                table: "ChatCitations");

            migrationBuilder.DropIndex(
                name: "IX_ChatCitations_InvestigationId",
                table: "ChatCitations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations");

            migrationBuilder.DropColumn(
                name: "AnswerInChat",
                table: "ManagedResearchJobs");

            migrationBuilder.DropColumn(
                name: "ManagedResearchJobId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "InvestigationId",
                table: "ChatCitations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations",
                sql: "(\"SourceDocumentId\" IS NOT NULL AND \"WebEvidenceSnapshotId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NOT NULL)");
        }
    }
}
