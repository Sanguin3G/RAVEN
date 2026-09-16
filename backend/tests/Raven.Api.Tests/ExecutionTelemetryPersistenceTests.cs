using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Raven.Api.Data;
using Raven.Api.Features.Research.Events;

namespace Raven.Api.Tests;

public sealed class ExecutionTelemetryPersistenceTests
{
    [Fact]
    public async Task Saturated_queue_drops_diagnostics_without_failing_the_writer()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var writer = new BufferedResearchEventWriter(loggerFactory.CreateLogger<BufferedResearchEventWriter>());

        for (var index = 0; index <= BufferedResearchEventWriter.QueueCapacity; index++)
        {
            await writer.WriteAsync(new ResearchEvent
            {
                Category = ResearchEventCategory.Search,
                Operation = "web_search",
                Status = ResearchEventStatus.Completed
            });
        }

        Assert.Equal(1, writer.DroppedCount);
    }

    [Fact]
    public async Task Buffered_writer_persists_preceding_events_in_one_batched_save_after_flush()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var saves = new SaveCounterInterceptor();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RavenDbContext>(options => options.UseSqlite(connection).AddInterceptors(saves));
        services.AddSingleton<BufferedResearchEventWriter>();
        services.AddSingleton<IResearchEventWriter>(provider => provider.GetRequiredService<BufferedResearchEventWriter>());
        services.AddSingleton<IResearchTelemetryFlusher>(provider => provider.GetRequiredService<BufferedResearchEventWriter>());
        services.AddSingleton<ResearchTelemetryBackgroundService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RavenDbContext>().Database.EnsureCreatedAsync();
        }

        var worker = provider.GetRequiredService<ResearchTelemetryBackgroundService>();
        await worker.StartAsync(CancellationToken.None);
        saves.Reset();
        var writer = provider.GetRequiredService<IResearchEventWriter>();
        // This fixture represents many provider-boundary operations in one run.
        // The invariant is a single batch save, rather than one SaveChanges per row.
        for (var index = 0; index < 20; index++)
        {
            await writer.WriteAsync(new ResearchEvent
            {
                Category = ResearchEventCategory.Search,
                Operation = "web_search",
                Status = ResearchEventStatus.Completed,
                InputSummary = $"query-{index}"
            });
        }

        await provider.GetRequiredService<IResearchTelemetryFlusher>().FlushAsync();
        await using (var scope = provider.CreateAsyncScope())
        {
            var events = await scope.ServiceProvider.GetRequiredService<RavenDbContext>().ResearchEvents
                .OrderBy(item => item.Sequence)
                .ToArrayAsync();
            Assert.Equal(20, events.Length);
            Assert.Equal(events.Select(item => item.Sequence).Order().ToArray(), events.Select(item => item.Sequence).ToArray());
            Assert.All(events, item => Assert.True(item.Sequence > 0));
        }

        Assert.Equal(1, saves.Count);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Persistence_failure_and_shutdown_drain_do_not_surface_to_the_writer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RavenDbContext>(options => options.UseSqlite(connection).AddInterceptors(new ThrowOnSaveInterceptor()));
        services.AddSingleton<BufferedResearchEventWriter>();
        services.AddSingleton<IResearchEventWriter>(provider => provider.GetRequiredService<BufferedResearchEventWriter>());
        services.AddSingleton<IResearchTelemetryFlusher>(provider => provider.GetRequiredService<BufferedResearchEventWriter>());
        services.AddSingleton<ResearchTelemetryBackgroundService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RavenDbContext>().Database.EnsureCreatedAsync();
        }

        var worker = provider.GetRequiredService<ResearchTelemetryBackgroundService>();
        await worker.StartAsync(CancellationToken.None);
        await provider.GetRequiredService<IResearchEventWriter>().WriteAsync(new ResearchEvent
        {
            Category = ResearchEventCategory.AI,
            Operation = "company_chat",
            Status = ResearchEventStatus.Completed
        });

        // Flush completes after the failed batch is observed; diagnostic loss
        // must never throw back into a product caller.
        await provider.GetRequiredService<IResearchTelemetryFlusher>().FlushAsync();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Shutdown_drains_queued_events_best_effort()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RavenDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<BufferedResearchEventWriter>();
        services.AddSingleton<IResearchEventWriter>(provider => provider.GetRequiredService<BufferedResearchEventWriter>());
        services.AddSingleton<IResearchTelemetryFlusher>(provider => provider.GetRequiredService<BufferedResearchEventWriter>());
        services.AddSingleton<ResearchTelemetryBackgroundService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RavenDbContext>().Database.EnsureCreatedAsync();
        }

        var worker = provider.GetRequiredService<ResearchTelemetryBackgroundService>();
        await worker.StartAsync(CancellationToken.None);
        var writer = provider.GetRequiredService<IResearchEventWriter>();
        for (var index = 0; index < 3; index++)
        {
            await writer.WriteAsync(new ResearchEvent { Category = ResearchEventCategory.Crawl, Operation = "page_crawl", Status = ResearchEventStatus.Completed });
        }

        await worker.StopAsync(CancellationToken.None);
        await using var readScope = provider.CreateAsyncScope();
        Assert.Equal(3, await readScope.ServiceProvider.GetRequiredService<RavenDbContext>().ResearchEvents.CountAsync());
    }

    private sealed class SaveCounterInterceptor : SaveChangesInterceptor
    {
        private int count;
        public int Count => Volatile.Read(ref count);
        public void Reset() => Interlocked.Exchange(ref count, 0);
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowOnSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<int>>(new InvalidOperationException("test telemetry store failure"));
    }
}
