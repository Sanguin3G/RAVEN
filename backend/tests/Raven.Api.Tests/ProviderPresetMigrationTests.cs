using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Settings;

namespace Raven.Api.Tests;

public sealed class ProviderPresetMigrationTests
{
    [Fact]
    public async Task Rename_migration_preserves_settings_while_rewriting_legacy_string_value()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<RavenDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new RavenDbContext(options))
        {
            await context.Database.MigrateAsync("20260911102454_AddDay5ResearchModesAndArchiving");
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO ResearchSettings
                    (Id, GroundingMode, ProfileModel, GroundingModel, DeepResearchModel,
                     AiSourceRerankingEnabled, ProviderPreset, SearchProviderPriority,
                     CrawlerProviderPriority, UpdatedAt)
                VALUES
                    ('research', 'Always', 'gemini-3.8-flash', 'gemini-3.5-flash-lite',
                     'gemini-3.5-flash-lite', 0, 'Balanced', '["brave","exa"]',
                     '["crawl4ai-local","firecrawl"]', '2026-09-11T00:00:00.0000000+00:00');
                """);
        }

        await using (var context = new RavenDbContext(options))
        {
            await context.Database.MigrateAsync();

            var settings = await context.ResearchSettings
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(ProviderPreset.Resilient, settings.ProviderPreset);
            Assert.Equal(GroundingMode.Always, settings.GroundingMode);
            Assert.Equal("gemini-3.8-flash", settings.ProfileModel);
            Assert.Equal("gemini-3.5-flash-lite", settings.GroundingModel);
            Assert.False(settings.AiSourceRerankingEnabled);
            Assert.Equal(["brave", "exa"], settings.SearchProviderPriority);
            Assert.Equal(["crawl4ai-local", "firecrawl"], settings.CrawlerProviderPriority);
        }
    }
}
