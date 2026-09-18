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
            Origin = request.Origin,
            Model = NormalizeOptionalModel(request.Model),
            Provider = NormalizeOptionalText(request.Provider),
            Objective = NormalizeOptionalText(request.Objective),
            CompletedAt = request.CompletedAt,
            ManagedResearchJobId = NormalizeOptionalText(request.ManagedResearchJobId),
            SourceCount = sourceDocumentIds.Length,
            SourceDocumentIdsJson = JsonSerializer.Serialize(sourceDocumentIds)
        };

        foreach (var sourceDocumentId in sourceDocumentIds)
        {
            artifact.SourceDocumentIds.Add(sourceDocumentId);
        }

        foreach (var sourceLead in request.SourceLeads ?? [])
        {
            artifact.SourceLeads.Add(NormalizeSourceLead(sourceLead));
        }

        foreach (var claim in request.Claims ?? [])
        {
            artifact.Claims.Add(new ResearchClaim(
                claim.Field.Trim(),
                claim.Statement.Trim(),
                claim.SupportingSourceLeadIds?.Distinct().ToArray(),
                NormalizeOptionalText(claim.Confidence),
                NormalizeOptionalText(claim.Notes)));
        }

        foreach (var uncertainty in request.Uncertainties ?? [])
        {
            artifact.Uncertainties.Add(uncertainty.Trim());
        }

        foreach (var metadata in request.ProviderMetadata ?? new Dictionary<string, string>())
        {
            artifact.ProviderMetadata[metadata.Key.Trim()] = metadata.Value.Trim();
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

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static ResearchSourceLead NormalizeSourceLead(ResearchSourceLead sourceLead)
    {
        ArgumentNullException.ThrowIfNull(sourceLead);

        return sourceLead with
        {
            Url = sourceLead.Url.Trim(),
            Title = NormalizeOptionalText(sourceLead.Title),
            Publisher = NormalizeOptionalText(sourceLead.Publisher),
            SourceType = NormalizeOptionalText(sourceLead.SourceType),
            Supports = NormalizeOptionalText(sourceLead.Supports)
        };
    }

    private static void ValidateScopeId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The ID must be a non-empty ID.", parameterName);
        }
    }
}
