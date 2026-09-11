using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.DeepResearch;

/// <summary>Persistent, sanitized activity row for polling clients.</summary>
public sealed class DeepResearchActivityRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DeepResearchRunId { get; init; }
    public long Sequence { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string Type { get; init; }
    public DeepResearchActivityStatus Status { get; init; }
    public required string Label { get; init; }
    public string? Detail { get; init; }
    public string? Provider { get; init; }
    public required string SourceDocumentIdsJson { get; init; } = "[]";
}

public sealed class EfDeepResearchRunStore(RavenDbContext dbContext) : IDeepResearchRunStore
{
    public async Task AddAsync(DeepResearchRun run, CancellationToken cancellationToken = default)
    {
        dbContext.DeepResearchRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<DeepResearchRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default) =>
        dbContext.DeepResearchRuns.AsNoTracking().SingleOrDefaultAsync(run => run.Id == runId, cancellationToken);

    public async Task UpdateAsync(DeepResearchRun run, CancellationToken cancellationToken = default)
    {
        dbContext.DeepResearchRuns.Update(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeepResearchRun>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await dbContext.DeepResearchRuns.AsNoTracking()
            .Where(run => run.CompanyId == companyId)
            .OrderByDescending(run => run.CreatedAt)
            .ToListAsync(cancellationToken);
}

public sealed class EfDeepResearchActivityStore(RavenDbContext dbContext) : IDeepResearchActivityStore, IDeepResearchActivitySink
{
    public async Task AddAsync(Guid runId, DeepResearchActivityEvent activity, CancellationToken cancellationToken = default)
    {
        var safe = DeepResearchActivitySanitizer.Sanitize(activity);
        var next = (await dbContext.DeepResearchActivities
            .Where(item => item.DeepResearchRunId == runId)
            .Select(item => (long?)item.Sequence)
            .MaxAsync(cancellationToken) ?? -1) + 1;
        dbContext.DeepResearchActivities.Add(new DeepResearchActivityRecord
        {
            DeepResearchRunId = runId,
            Sequence = next,
            Type = safe.Type,
            Status = safe.Status,
            Label = safe.Label,
            Detail = safe.Detail,
            Provider = safe.Provider,
            SourceDocumentIdsJson = JsonSerializer.Serialize(safe.SourceDocumentIds)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task PublishAsync(Guid runId, DeepResearchActivityEvent activity, CancellationToken cancellationToken = default) =>
        AddAsync(runId, activity, cancellationToken);

    public async Task<IReadOnlyList<DeepResearchActivityEvent>> ListAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var items = await dbContext.DeepResearchActivities.AsNoTracking()
            .Where(item => item.DeepResearchRunId == runId)
            .OrderBy(item => item.Sequence)
            .ToListAsync(cancellationToken);
        return items.Select(item => new DeepResearchActivityEvent(
            item.Type,
            item.Status,
            item.Label,
            item.Detail,
            item.Provider,
            ReadIds(item.SourceDocumentIdsJson))).ToArray();
    }

    private static IReadOnlyList<Guid> ReadIds(string json)
    {
        try { return JsonSerializer.Deserialize<Guid[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
