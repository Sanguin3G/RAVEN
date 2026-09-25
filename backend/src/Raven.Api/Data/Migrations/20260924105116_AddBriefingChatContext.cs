using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBriefingChatContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResearchContextAttachments_CompanyId_ConversationId_InvestigationId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations");

            migrationBuilder.AlterColumn<Guid>(
                name: "InvestigationId",
                table: "ResearchContextAttachments",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "BriefingId",
                table: "ResearchContextAttachments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BriefingVersionId",
                table: "ResearchContextAttachments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BriefingVersionId",
                table: "ChatCitations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchContextAttachments_BriefingId",
                table: "ResearchContextAttachments",
                column: "BriefingId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchContextAttachments_BriefingVersionId",
                table: "ResearchContextAttachments",
                column: "BriefingVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchContextAttachments_CompanyId_ConversationId_BriefingId",
                table: "ResearchContextAttachments",
                columns: new[] { "CompanyId", "ConversationId", "BriefingId" },
                unique: true,
                filter: "\"BriefingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchContextAttachments_CompanyId_ConversationId_InvestigationId",
                table: "ResearchContextAttachments",
                columns: new[] { "CompanyId", "ConversationId", "InvestigationId" },
                unique: true,
                filter: "\"InvestigationId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResearchContextAttachments_ExactlyOneContext",
                table: "ResearchContextAttachments",
                sql: "(\"InvestigationId\" IS NOT NULL AND \"BriefingId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"InvestigationId\" IS NULL AND \"BriefingId\" IS NOT NULL AND \"BriefingVersionId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ChatCitations_BriefingVersionId",
                table: "ChatCitations",
                column: "BriefingVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatCitations_ChatMessageId_BriefingVersionId",
                table: "ChatCitations",
                columns: new[] { "ChatMessageId", "BriefingVersionId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations",
                sql: "(\"SourceDocumentId\" IS NOT NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NOT NULL AND \"InvestigationId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NOT NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL AND \"BriefingVersionId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatCitations_ResearchBriefingVersions_BriefingVersionId",
                table: "ChatCitations",
                column: "BriefingVersionId",
                principalTable: "ResearchBriefingVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchContextAttachments_ResearchBriefingVersions_BriefingVersionId",
                table: "ResearchContextAttachments",
                column: "BriefingVersionId",
                principalTable: "ResearchBriefingVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchContextAttachments_ResearchBriefings_BriefingId",
                table: "ResearchContextAttachments",
                column: "BriefingId",
                principalTable: "ResearchBriefings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatCitations_ResearchBriefingVersions_BriefingVersionId",
                table: "ChatCitations");

            migrationBuilder.DropForeignKey(
                name: "FK_ResearchContextAttachments_ResearchBriefingVersions_BriefingVersionId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_ResearchContextAttachments_ResearchBriefings_BriefingId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ResearchContextAttachments_BriefingId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ResearchContextAttachments_BriefingVersionId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ResearchContextAttachments_CompanyId_ConversationId_BriefingId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ResearchContextAttachments_CompanyId_ConversationId_InvestigationId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResearchContextAttachments_ExactlyOneContext",
                table: "ResearchContextAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ChatCitations_BriefingVersionId",
                table: "ChatCitations");

            migrationBuilder.DropIndex(
                name: "IX_ChatCitations_ChatMessageId_BriefingVersionId",
                table: "ChatCitations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations");

            migrationBuilder.DropColumn(
                name: "BriefingId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropColumn(
                name: "BriefingVersionId",
                table: "ResearchContextAttachments");

            migrationBuilder.DropColumn(
                name: "BriefingVersionId",
                table: "ChatCitations");

            migrationBuilder.AlterColumn<Guid>(
                name: "InvestigationId",
                table: "ResearchContextAttachments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchContextAttachments_CompanyId_ConversationId_InvestigationId",
                table: "ResearchContextAttachments",
                columns: new[] { "CompanyId", "ConversationId", "InvestigationId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations",
                sql: "(\"SourceDocumentId\" IS NOT NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NOT NULL AND \"InvestigationId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NOT NULL)");
        }
    }
}
