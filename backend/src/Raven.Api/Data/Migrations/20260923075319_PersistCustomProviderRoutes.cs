using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistCustomProviderRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomCrawlerProviderPriority",
                table: "ResearchSettings",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "CustomSearchProviderPriority",
                table: "ResearchSettings",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "[]");

            // Preserve an existing route when the user was already in Custom.
            // Named presets receive the normal resilient route as their saved
            // Custom starting point; historical active priorities are not
            // reinterpreted as user-defined Custom choices.
            migrationBuilder.Sql("""
                UPDATE "ResearchSettings"
                SET "CustomSearchProviderPriority" = CASE
                        WHEN "ProviderPreset" = 'Custom' THEN "SearchProviderPriority"
                        ELSE '["brave","exa"]'
                    END,
                    "CustomCrawlerProviderPriority" = CASE
                        WHEN "ProviderPreset" = 'Custom' THEN "CrawlerProviderPriority"
                        ELSE '["crawl4ai-local","exa"]'
                    END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomCrawlerProviderPriority",
                table: "ResearchSettings");

            migrationBuilder.DropColumn(
                name: "CustomSearchProviderPriority",
                table: "ResearchSettings");
        }
    }
}
