using System.Text.Json;

namespace Raven.Api.Features.Research.SavedArtifacts;

/// <summary>
/// Creates and reads saved research outputs without touching accepted profile
/// versions. Source references are accepted only after company ownership is
/// checked through <see cref="ISourceDocumentOwnershipReader"/>.
/// </summary>
public sealed class SavedResearchArtifactService : ISavedResearchArtifactService
{
    private readonly ISavedResearchArtifactStore store;
    private readonly ISourceDocumentOwnershipReader sourceOwnershipReader;
    private readonly ISavedResearchArtifactClock clock;

    public SavedResearchArtifactService(
        ISavedResearchArtifactStore store,
        ISourceDocumentOwnershipReader sourceOwnershipReader,
        ISavedResearchArtifactClock clock)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.sourceOwnershipReader = sourceOwnershipReader ?? throw new ArgumentNullException(nameof(sourceOwnershipReader));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<SavedResearchArtifact> CreateAsync(
        SavedResearchArtifactRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = SavedResearchArtifactValidation.Validate(request).ToList();
        if (errors.Count > 0)
        {
            throw new SavedResearchArtifactValidationException(errors);
        }

        var sourceDocumentIds = (request.SourceDocumentIds ?? [])
            .Distinct()
            .ToArray();
        var ownedSourceDocumentIds = await sourceOwnershipReader.GetOwnedSourceDocumentIdsAsync(
            request.CompanyId,
            sourceDocumentIds,
            cancellationToken);

        var unownedSourceDocumentIds = sourceDocumentIds
            .Where(sourceDocumentId => !ownedSourceDocumentIds.Contains(sourceDocumentId))
            .ToArray();
        if (unownedSourceDocumentIds.Length > 0)
        {
            errors.AddRange(unownedSourceDocumentIds.Select(sourceDocumentId =>
                $"Source document '{sourceDocumentId}' is not owned by the company."));
            throw new SavedResearchArtifactValidationException(errors);
        }

        var artifact = new SavedResearchArtifact
        {
            CompanyId = request.CompanyId,
            ConversationId = request.ConversationId,
            DeepResearchRunId = request.DeepResearchRunId,
            Title = request.Title.Trim(),
            Question = request.Question.Trim(),
            Summary = request.Summary.Trim(),
            CreatedAt = clock.UtcNow,
            ResearchType = request.ResearchType,
            Model = NormalizeOptionalModel(request.Model),
            SourceCount = sourceDocumentIds.Length,
            SourceDocumentIdsJson = JsonSerializer.Serialize(sourceDocumentIds)
        };

        foreach (var sourceDocumentId in sourceDocumentIds)
        {
            artifact.SourceDocumentIds.Add(sourceDocumentId);
        }

        await store.SaveAsync(artifact, cancellationToken);
        return artifact;
    }

    public Task<SavedResearchArtifact?> GetAsync(
        Guid companyId,
        Guid artifactId,
        CancellationToken cancellationToken = default)
    {
        ValidateScopeId(companyId, nameof(companyId));
        ValidateScopeId(artifactId, nameof(artifactId));
        return store.GetAsync(companyId, artifactId, cancellationToken);
    }

    public Task<IReadOnlyList<SavedResearchArtifact>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        ValidateScopeId(companyId, nameof(companyId));
        return store.ListAsync(companyId, cancellationToken);
    }

    private static string? NormalizeOptionalModel(string? model)
    {
        var normalized = model?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void ValidateScopeId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The ID must be a non-empty ID.", parameterName);
        }
    }
}
