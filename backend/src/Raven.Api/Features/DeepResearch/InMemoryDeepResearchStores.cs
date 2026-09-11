using System.Collections.Concurrent;

namespace Raven.Api.Features.DeepResearch;

/// <summary>
/// Small process-local store for tests and local composition. Production
/// wiring should replace it with an EF implementation without changing the
/// execution service or contracts.
/// </summary>
public sealed class InMemoryDeepResearchRunStore : IDeepResearchRunStore
{
    private readonly ConcurrentDictionary<Guid, DeepResearchRun> runs = new();

    public Task AddAsync(DeepResearchRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        if (!runs.TryAdd(run.Id, run))
        {
            throw new InvalidOperationException($"Deep Research run {run.Id} already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<DeepResearchRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        runs.TryGetValue(runId, out var run);
        return Task.FromResult(run);
    }

    public Task UpdateAsync(DeepResearchRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        runs[run.Id] = run;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DeepResearchRun>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DeepResearchRun> result = runs.Values
            .Where(run => run.CompanyId == companyId)
            .OrderByDescending(run => run.CreatedAt)
            .ToArray();
        return Task.FromResult(result);
    }
}

public sealed class InMemoryDeepResearchActivityStore : IDeepResearchActivityStore, IDeepResearchActivitySink
{
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<DeepResearchActivityEvent>> activities = new();

    public Task AddAsync(Guid runId, DeepResearchActivityEvent activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        cancellationToken.ThrowIfCancellationRequested();
        activities.GetOrAdd(runId, static _ => new ConcurrentQueue<DeepResearchActivityEvent>()).Enqueue(
            DeepResearchActivitySanitizer.Sanitize(activity));
        return Task.CompletedTask;
    }

    public Task PublishAsync(Guid runId, DeepResearchActivityEvent activity, CancellationToken cancellationToken = default) =>
        AddAsync(runId, activity, cancellationToken);

    public Task<IReadOnlyList<DeepResearchActivityEvent>> ListAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DeepResearchActivityEvent> result = activities.TryGetValue(runId, out var queue)
            ? queue.ToArray()
            : [];
        return Task.FromResult(result);
    }
}

public sealed class CompositeDeepResearchActivitySink(params IDeepResearchActivitySink[] sinks) : IDeepResearchActivitySink
{
    public async Task PublishAsync(Guid runId, DeepResearchActivityEvent activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        foreach (var sink in sinks.Where(sink => sink is not null))
        {
            await sink.PublishAsync(runId, activity, cancellationToken);
        }
    }
}
