using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatModelSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChatModel",
                table: "ResearchSettings",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "gemini-3.5-flash-lite");

            // Chat previously read ProfileModel. Preserve each workspace's
            // effective chat choice before the two roles become independent.
            migrationBuilder.Sql("UPDATE ResearchSettings SET ChatModel = ProfileModel;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChatModel",
                table: "ResearchSettings");
        }
    }
}
