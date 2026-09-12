using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;

namespace Raven.Api.Tests;

public sealed class ResearchExecutionSummaryTests
{
    [Fact]
    public void Aggregate_counts_canonical_operations_and_nullable_usage()
    {
        var started = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
        var runId = Guid.NewGuid();
        var events = new[]
        {
            new ResearchEvent
            {
                ResearchRunId = runId,
                Timestamp = started,
                DurationMs = 482,
                Category = ResearchEventCategory.Search,
                Operation = "web_search",
                Status = ResearchEventStatus.Completed,
                MetadataJson = "{\"providerAttempts\":2}"
            },
            new ResearchEvent
            {
                ResearchRunId = runId,
                Timestamp = started.AddSeconds(1),
                DurationMs = 1_800,
                Category = ResearchEventCategory.Crawl,
                Operation = "page_crawl",
                Status = ResearchEventStatus.Completed
            },
            new ResearchEvent
            {
                ResearchRunId = runId,
                Timestamp = started.AddSeconds(2),
                DurationMs = 1_200,
                Category = ResearchEventCategory.AI,
                Operation = "identity_resolution",
                Status = ResearchEventStatus.Completed,
                InputTokens = 2_341,
                OutputTokens = 312,
                CachedTokens = 50,
                ThinkingTokens = 18
            },
            new ResearchEvent
            {
                ResearchRunId = runId,
                Timestamp = started.AddSeconds(3),
                Category = ResearchEventCategory.Research,
                Operation = "provider_fallback",
                Status = ResearchEventStatus.Completed
            }
        };

        var summary = ResearchExecutionSummaryAggregator.Aggregate(
            runId,
            started,
            started.AddSeconds(4),
            documentsAcquired: 3,
            events);

        Assert.Equal(4_000, summary.TotalWallClockDurationMs);
        Assert.Equal(1, summary.SearchCalls);
        Assert.Equal(4, summary.ProviderAttempts);
        Assert.Equal(1, summary.Fallbacks);
        Assert.Equal(1, summary.CrawlCalls);
        Assert.Equal(1, summary.SuccessfulCrawls);
        Assert.Equal(0, summary.FailedCrawls);
        Assert.Equal(1, summary.AiCalls);
        Assert.Equal(2_341, summary.InputTokens);
        Assert.Equal(312, summary.OutputTokens);
        Assert.Equal(50, summary.CachedTokens);
        Assert.Equal(18, summary.ThinkingTokens);
        Assert.Equal(3, summary.DocumentsAcquired);
        Assert.Equal(4, summary.TelemetryOperations);
    }

    [Fact]
    public void Aggregate_reads_legacy_lifecycle_rows_without_double_counting_calls()
    {
        var started = DateTimeOffset.UtcNow.AddSeconds(-5);
        var events = new[]
        {
            new ResearchEvent { Timestamp = started, Category = ResearchEventCategory.SearchRequested, Status = ResearchEventStatus.Working },
            new ResearchEvent { Timestamp = started.AddMilliseconds(100), Category = ResearchEventCategory.SearchCompleted, Status = ResearchEventStatus.Completed },
            new ResearchEvent { Timestamp = started.AddMilliseconds(200), Category = ResearchEventCategory.CrawlRequested, Status = ResearchEventStatus.Working },
            new ResearchEvent { Timestamp = started.AddMilliseconds(400), Category = ResearchEventCategory.CrawlFailed, Status = ResearchEventStatus.Failed }
        };

        var summary = ResearchExecutionSummaryAggregator.Aggregate(
            Guid.NewGuid(), started, started.AddSeconds(1), 0, events);

        Assert.Equal(1, summary.SearchCalls);
        Assert.Equal(1, summary.CrawlCalls);
        Assert.Equal(1, summary.FailedCrawls);
        Assert.Equal(1, summary.Failures);
        Assert.Equal(2, summary.ProviderAttempts);
    }

    [Fact]
    public async Task Writer_allocates_sequence_without_database_max_lookup()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RavenDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new RavenDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var writer = new EfResearchEventWriter(context);

        await writer.WriteAsync(new ResearchEvent
        {
            Category = ResearchEventCategory.Search,
            Operation = "web_search",
            Status = ResearchEventStatus.Completed
        });
        await writer.WriteAsync(new ResearchEvent
        {
            Category = ResearchEventCategory.Crawl,
            Operation = "page_crawl",
            Status = ResearchEventStatus.Completed
        });

        var sequences = await context.ResearchEvents
            .AsNoTracking()
            .OrderBy(item => item.Sequence)
            .Select(item => item.Sequence)
            .ToArrayAsync();
        Assert.Equal(2, sequences.Length);
        Assert.All(sequences, sequence => Assert.True(sequence > 0));
        Assert.True(sequences[1] > sequences[0]);
    }
}
