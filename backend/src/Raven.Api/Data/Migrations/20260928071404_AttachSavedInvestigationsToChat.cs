using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AttachSavedInvestigationsToChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ResearchContextAttachments_ExactlyOneContext",
                table: "ResearchContextAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations");

            migrationBuilder.AddColumn<Guid>(
                name: "SavedResearchArtifactId",
                table: "ResearchContextAttachments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SavedResearchArtifactId",
                table: "ChatCitations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchContextAttachments_CompanyId_ConversationId_SavedResearchArtifactId",
                table: "ResearchContextAttachments",
                columns: new[] { "CompanyId", "ConversationId", "SavedResearchArtifactId" },
                unique: true,
                filter: "\"SavedResearchArtifactId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchContextAttachments_SavedResearchArtifactId",
                table: "ResearchContextAttachments",
                column: "SavedResearchArtifactId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResearchContextAttachments_ExactlyOneContext",
                table: "ResearchContextAttachments",
                sql: "(\"InvestigationId\" IS NOT NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NOT NULL AND \"BriefingId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingId\" IS NOT NULL AND \"BriefingVersionId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ChatCitations_ChatMessageId_SavedResearchArtifactId",
                table: "ChatCitations",
                columns: new[] { "ChatMessageId", "SavedResearchArtifactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatCitations_SavedResearchArtifactId",
                table: "ChatCitations",
                column: "SavedResearchArtifactId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations",
                sql: "(\"SourceDocumentId\" IS NOT NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NOT NULL AND \"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NOT NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NOT NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingVersionId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatCitations_SavedResearchArtifacts_SavedResearchArtifactId",
                table: "ChatCitations",
                column: "SavedResearchArtifactId",
                principalTable: "SavedResearchArtifacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchContextAttachments_SavedResearchArtifacts_SavedResearchArtifactId",
                table: "ResearchContextAttachments",
                column: "SavedResearchArtifactId",
                principalTable: "SavedResearchArtifacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatCitations_SavedResearchArtifacts_SavedResearchArtifactId",
                table: "ChatCitations");

            migrationBuilder.DropForeignKey(
                name: "FK_ResearchContextAttachments_SavedResearchArtifacts_SavedResearchArtifactId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ResearchContextAttachments_CompanyId_ConversationId_SavedResearchArtifactId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ResearchContextAttachments_SavedResearchArtifactId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResearchContextAttachments_ExactlyOneContext",
                table: "ResearchContextAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ChatCitations_ChatMessageId_SavedResearchArtifactId",
                table: "ChatCitations");

            migrationBuilder.DropIndex(
                name: "IX_ChatCitations_SavedResearchArtifactId",
                table: "ChatCitations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations");

            migrationBuilder.DropColumn(
                name: "SavedResearchArtifactId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropColumn(
                name: "SavedResearchArtifactId",
                table: "ChatCitations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResearchContextAttachments_ExactlyOneContext",
                table: "ResearchContextAttachments",
                sql: "(\"InvestigationId\" IS NOT NULL AND \"BriefingId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"InvestigationId\" IS NULL AND \"BriefingId\" IS NOT NULL AND \"BriefingVersionId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations",
                sql: "(\"SourceDocumentId\" IS NOT NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NOT NULL AND \"InvestigationId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NOT NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL AND \"BriefingVersionId\" IS NOT NULL)");
        }
    }
}
