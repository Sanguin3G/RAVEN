using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatWebEvidenceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SourceDocumentId",
                table: "ChatCitations",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "WebEvidenceSnapshotId",
                table: "ChatCitations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatWebEvidenceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChatMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NormalizedUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SearchSnippet = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ContentExcerpt = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    SearchProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CrawlerProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SearchRank = table.Column<int>(type: "INTEGER", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatWebEvidenceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatWebEvidenceSnapshots_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatCitations_ChatMessageId_WebEvidenceSnapshotId",
                table: "ChatCitations",
                columns: new[] { "ChatMessageId", "WebEvidenceSnapshotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatCitations_WebEvidenceSnapshotId",
                table: "ChatCitations",
                column: "WebEvidenceSnapshotId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations",
                sql: "(\"SourceDocumentId\" IS NOT NULL AND \"WebEvidenceSnapshotId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ChatWebEvidenceSnapshots_ChatMessageId_NormalizedUrl",
                table: "ChatWebEvidenceSnapshots",
                columns: new[] { "ChatMessageId", "NormalizedUrl" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatCitations_ChatWebEvidenceSnapshots_WebEvidenceSnapshotId",
                table: "ChatCitations",
                column: "WebEvidenceSnapshotId",
                principalTable: "ChatWebEvidenceSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatCitations_ChatWebEvidenceSnapshots_WebEvidenceSnapshotId",
                table: "ChatCitations");

            migrationBuilder.DropTable(
                name: "ChatWebEvidenceSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_ChatCitations_ChatMessageId_WebEvidenceSnapshotId",
                table: "ChatCitations");

            migrationBuilder.DropIndex(
                name: "IX_ChatCitations_WebEvidenceSnapshotId",
                table: "ChatCitations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatCitations_ExactlyOneEvidence",
                table: "ChatCitations");

            migrationBuilder.DropColumn(
                name: "WebEvidenceSnapshotId",
                table: "ChatCitations");

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceDocumentId",
                table: "ChatCitations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
