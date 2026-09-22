using System.Threading.Channels;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>Reference in-memory store for tests and local composition.</summary>
public sealed class InMemoryManagedResearchJobStore : IManagedResearchJobStore
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, ManagedResearchJob> jobs = [];

    public Task AddAsync(ManagedResearchJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            jobs.Add(job.Id, Clone(job));
        }

        return Task.CompletedTask;
    }

    public Task<ManagedResearchJob?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult(jobs.TryGetValue(jobId, out var job) ? Clone(job) : null);
        }
    }

    public Task<ManagedResearchJob?> GetAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult(jobs.TryGetValue(jobId, out var job) && job.CompanyId == companyId ? Clone(job) : null);
        }
    }

    public Task UpdateAsync(ManagedResearchJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!jobs.ContainsKey(job.Id))
            {
                throw new KeyNotFoundException("The managed research job was not found.");
            }

            jobs[job.Id] = Clone(job);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ManagedResearchJob>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            IReadOnlyList<ManagedResearchJob> result = jobs.Values
                .Where(job => !job.IsTerminal)
                .OrderBy(job => job.CreatedAt)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<ManagedResearchJob>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            IReadOnlyList<ManagedResearchJob> result = jobs.Values
                .Where(job => job.CompanyId == companyId)
                .OrderByDescending(job => job.CreatedAt)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    private static ManagedResearchJob Clone(ManagedResearchJob job) => new()
    {
        Id = job.Id,
        CompanyId = job.CompanyId,
        ConversationId = job.ConversationId,
        ChatMessageId = job.ChatMessageId,
        Objective = job.Objective,
        ProviderQuery = job.ProviderQuery,
        Effort = job.Effort,
        Purpose = job.Purpose,
        Status = job.Status,
        Provider = job.Provider,
        ProviderRunId = job.ProviderRunId,
        ProviderRunStatus = job.ProviderRunStatus,
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
        CancelRequestedAt = job.CancelRequestedAt,
        ResultJson = job.ResultJson,
        Error = job.Error,
        ProviderCostDollars = job.ProviderCostDollars,
        InvestigationId = job.InvestigationId
    };
}

/// <summary>Reference in-memory investigation store for tests and local composition.</summary>
public sealed class InMemoryManagedResearchInvestigationStore : IManagedResearchInvestigationStore
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, ManagedResearchInvestigation> investigations = [];

    public Task SaveAsync(ManagedResearchInvestigation investigation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(investigation);
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            investigations[investigation.Id] = Clone(investigation);
        }

        return Task.CompletedTask;
    }

    public Task<ManagedResearchInvestigation?> GetAsync(Guid companyId, Guid investigationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult(investigations.TryGetValue(investigationId, out var item) && item.CompanyId == companyId
                ? Clone(item)
                : null);
        }
    }

    public Task<IReadOnlyList<ManagedResearchInvestigation>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            IReadOnlyList<ManagedResearchInvestigation> result = investigations.Values
                .Where(item => item.CompanyId == companyId)
                .OrderByDescending(item => item.CompletedAt)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    private static ManagedResearchInvestigation Clone(ManagedResearchInvestigation item) => new()
    {
        Id = item.Id,
        JobId = item.JobId,
        CompanyId = item.CompanyId,
        ConversationId = item.ConversationId,
        ChatMessageId = item.ChatMessageId,
        Origin = item.Origin,
        Objective = item.Objective,
        Summary = item.Summary,
        ResultJson = item.ResultJson,
        CompletedAt = item.CompletedAt
    };
}

/// <summary>Simple read-only context reader used by deterministic tests.</summary>
public sealed class InMemoryManagedResearchCompanyContextReader : IManagedResearchCompanyContextReader
{
    private readonly Dictionary<Guid, ManagedResearchCompanyContext> contexts;

    public InMemoryManagedResearchCompanyContextReader(IEnumerable<ManagedResearchCompanyContext> contexts)
    {
        this.contexts = contexts.ToDictionary(context => context.CompanyId);
    }

    public Task<ManagedResearchCompanyContext?> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(contexts.TryGetValue(companyId, out var context) ? context : null);
    }
}

/// <summary>Channel-backed queue for managed research worker registration.</summary>
public sealed class ManagedResearchJobQueue : IManagedResearchJobQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken = default) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}
