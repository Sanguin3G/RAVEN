namespace Raven.Api.Features.Research.SavedArtifacts;

/// <summary>
/// Client input for creating a saved research artifact. SourceDocument IDs are
/// references only; the service verifies that every reference belongs to the
/// requested company before it persists anything.
/// </summary>
public record SavedResearchArtifactRequest(
    Guid CompanyId,
    string Title,
    string Question,
    string Summary,
    SavedResearchType ResearchType,
    string? Model = null,
    IReadOnlyList<Guid>? SourceDocumentIds = null,
    Guid? ConversationId = null,
    Guid? DeepResearchRunId = null)
{
    /// <summary>Vocabulary alias for callers that use Result instead of Summary.</summary>
    public string Result => Summary;
}

/// <summary>Explicit command name for API/application callers.</summary>
public sealed record CreateSavedResearchArtifactRequest(
    Guid CompanyId,
    string Title,
    string Question,
    string Summary,
    SavedResearchType ResearchType,
    string? Model = null,
    IReadOnlyList<Guid>? SourceDocumentIds = null,
    Guid? ConversationId = null,
    Guid? DeepResearchRunId = null)
    : SavedResearchArtifactRequest(
        CompanyId,
        Title,
        Question,
        Summary,
        ResearchType,
        Model,
        SourceDocumentIds,
        ConversationId,
        DeepResearchRunId);

/// <summary>
/// Response-shaped contract for a future endpoint. The current domain service
/// returns the entity so API wiring can choose its own transport shape.
/// </summary>
public sealed record SavedResearchArtifactResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ConversationId,
    Guid? DeepResearchRunId,
    string Title,
    string Question,
    string Summary,
    DateTimeOffset CreatedAt,
    SavedResearchType ResearchType,
    string? Model,
    int SourceCount,
    IReadOnlyList<Guid> SourceDocumentIds)
{
    public string Result => Summary;

    public static SavedResearchArtifactResponse FromEntity(SavedResearchArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return new SavedResearchArtifactResponse(
            artifact.Id,
            artifact.CompanyId,
            artifact.ConversationId,
            artifact.DeepResearchRunId,
            artifact.Title,
            artifact.Question,
            artifact.Summary,
            artifact.CreatedAt,
            artifact.ResearchType,
            artifact.Model,
            artifact.SourceCount,
            artifact.SourceDocumentIds.ToArray());
    }
}

/// <summary>
/// Persistence seam for saved artifacts. Implementations should return detached
/// values so a caller cannot mutate a persisted artifact without another save.
/// </summary>
public interface ISavedResearchArtifactStore
{
    Task SaveAsync(
        SavedResearchArtifact artifact,
        CancellationToken cancellationToken = default);

    Task<SavedResearchArtifact?> GetAsync(
        Guid companyId,
        Guid artifactId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedResearchArtifact>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Application-owned source lookup. It prevents a browser from attaching a
/// source ID belonging to another company. A batch method keeps the production
/// implementation to one ownership query rather than one query per source.
/// </summary>
public interface ISourceDocumentOwnershipReader
{
    Task<IReadOnlySet<Guid>> GetOwnedSourceDocumentIdsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceDocumentIds,
        CancellationToken cancellationToken = default);
}

public interface ISavedResearchArtifactClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemSavedResearchArtifactClock : ISavedResearchArtifactClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Reference store for isolated tests and local composition before an EF adapter
/// is integrated. It stores detached clones and scopes reads by company.
/// </summary>
public sealed class InMemorySavedResearchArtifactStore : ISavedResearchArtifactStore
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, SavedResearchArtifact> artifacts = [];

    public Task SaveAsync(
        SavedResearchArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            artifacts[artifact.Id] = artifact.Clone();
        }

        return Task.CompletedTask;
    }

    public Task<SavedResearchArtifact?> GetAsync(
        Guid companyId,
        Guid artifactId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            if (!artifacts.TryGetValue(artifactId, out var artifact) || artifact.CompanyId != companyId)
            {
                return Task.FromResult<SavedResearchArtifact?>(null);
            }

            return Task.FromResult<SavedResearchArtifact?>(artifact.Clone());
        }
    }

    public Task<IReadOnlyList<SavedResearchArtifact>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            IReadOnlyList<SavedResearchArtifact> result = artifacts.Values
                .Where(artifact => artifact.CompanyId == companyId)
                .OrderByDescending(artifact => artifact.CreatedAt)
                .ThenByDescending(artifact => artifact.Id)
                .Select(artifact => artifact.Clone())
                .ToArray();
            return Task.FromResult(result);
        }
    }
}

public interface ISavedResearchArtifactService
{
    Task<SavedResearchArtifact> CreateAsync(
        SavedResearchArtifactRequest request,
        CancellationToken cancellationToken = default);

    Task<SavedResearchArtifact?> GetAsync(
        Guid companyId,
        Guid artifactId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedResearchArtifact>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);
}
