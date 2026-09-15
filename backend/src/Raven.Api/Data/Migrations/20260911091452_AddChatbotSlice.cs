using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatbotSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResearchRuns_CompanyId_StartedAt",
                table: "ResearchRuns");

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "ProfileResearch");

            migrationBuilder.CreateTable(
                name: "ChatConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatConversations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatConversations_CompanyProfileVersions_ProfileVersionId",
                        column: x => x.ProfileVersionId,
                        principalTable: "CompanyProfileVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AnswerMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    WebLookupIncomplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    AiProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AiModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatCitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChatMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FieldPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Excerpt = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatCitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatCitations_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatCitations_SourceDocuments_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "SourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatToolExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChatMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Tool = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    InputSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    OutputSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatToolExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatToolExecutions_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatToolExecutions_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatCitations_ChatMessageId_SourceDocumentId",
                table: "ChatCitations",
                columns: new[] { "ChatMessageId", "SourceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatCitations_SourceDocumentId",
                table: "ChatCitations",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_CompanyId_UpdatedAt",
                table: "ChatConversations",
                columns: new[] { "CompanyId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ProfileVersionId",
                table: "ChatConversations",
                column: "ProfileVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConversationId_CreatedAt",
                table: "ChatMessages",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatToolExecutions_ChatMessageId_CreatedAt",
                table: "ChatToolExecutions",
                columns: new[] { "ChatMessageId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatToolExecutions_ResearchRunId",
                table: "ChatToolExecutions",
                column: "ResearchRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchRuns_CompanyId_Purpose_StartedAt",
                table: "ResearchRuns",
                columns: new[] { "CompanyId", "Purpose", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatCitations");

            migrationBuilder.DropTable(
                name: "ChatToolExecutions");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatConversations");

            migrationBuilder.DropIndex(
                name: "IX_ResearchRuns_CompanyId_Purpose_StartedAt",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "ResearchRuns");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchRuns_CompanyId_StartedAt",
                table: "ResearchRuns",
                columns: new[] { "CompanyId", "StartedAt" });
        }
    }
}
