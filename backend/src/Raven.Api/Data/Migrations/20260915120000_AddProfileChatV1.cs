using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Raven.Api.Data;

#nullable disable

namespace Raven.Api.Data.Migrations;

[DbContext(typeof(RavenDbContext))]
[Migration("20260915120000_AddProfileChatV1")]
public partial class AddProfileChatV1 : Migration
{
    /// <summary>
    /// Upgrades the original chatbot slice without recreating its tables.
    /// The previous migration is retained because existing local databases may
    /// already have applied it.
    /// </summary>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AnswerStatus",
            table: "ChatMessages",
            type: "TEXT",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FollowUpQuestion",
            table: "ChatMessages",
            type: "TEXT",
            maxLength: 1000,
            nullable: true);

        // The original slice created this index as non-unique. The current
        // entity model treats one source as one citation per assistant message.
        migrationBuilder.DropIndex(
            name: "IX_ChatCitations_ChatMessageId_SourceDocumentId",
            table: "ChatCitations");

        migrationBuilder.CreateIndex(
            name: "IX_ChatCitations_ChatMessageId_SourceDocumentId",
            table: "ChatCitations",
            columns: new[] { "ChatMessageId", "SourceDocumentId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ChatCitations_ChatMessageId_SourceDocumentId",
            table: "ChatCitations");

        migrationBuilder.CreateIndex(
            name: "IX_ChatCitations_ChatMessageId_SourceDocumentId",
            table: "ChatCitations",
            columns: new[] { "ChatMessageId", "SourceDocumentId" });

        migrationBuilder.DropColumn(name: "AnswerStatus", table: "ChatMessages");
        migrationBuilder.DropColumn(name: "FollowUpQuestion", table: "ChatMessages");
    }
}
