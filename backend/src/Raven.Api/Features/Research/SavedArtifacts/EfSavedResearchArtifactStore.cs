using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Research.SavedArtifacts;

public sealed class EfSavedResearchArtifactStore(RavenDbContext dbContext) : ISavedResearchArtifactStore
{
    public async Task SaveAsync(SavedResearchArtifact artifact, CancellationToken cancellationToken = default)
    {
        dbContext.SavedResearchArtifacts.Add(artifact);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SavedResearchArtifact?> GetAsync(Guid companyId, Guid artifactId, CancellationToken cancellationToken = default)
    {
        var artifact = await dbContext.SavedResearchArtifacts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == artifactId, cancellationToken);
        return artifact is null ? null : Hydrate(artifact);
    }

    public async Task<IReadOnlyList<SavedResearchArtifact>> ListAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        // SQLite cannot translate DateTimeOffset ordering. The list is company
        // scoped and intentionally small, so preserve a deterministic newest-
        // first result after the safe SQL filter has materialized the rows.
        var artifacts = await dbContext.SavedResearchArtifacts.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        return artifacts
            .OrderByDescending(item => item.CreatedAt)
            .Select(Hydrate)
            .ToArray();
    }

    private static SavedResearchArtifact Hydrate(SavedResearchArtifact artifact)
    {
        try
        {
            foreach (var sourceId in JsonSerializer.Deserialize<Guid[]>(artifact.SourceDocumentIdsJson) ?? [])
            {
                artifact.SourceDocumentIds.Add(sourceId);
            }
        }
        catch (JsonException)
        {
            // The artifact itself remains readable; its corrupted optional links
            // are not treated as valid evidence.
        }

        TryHydrate(artifact.ProviderMetadataJson, artifact.ProviderMetadata);
        TryHydrate(artifact.SourceLeadsJson, artifact.SourceLeads);
        TryHydrate(artifact.ClaimsJson, artifact.Claims);
        TryHydrate(artifact.UncertaintiesJson, artifact.Uncertainties);

        return artifact;
    }

    private static void TryHydrate<T>(string json, ICollection<T> target)
    {
        try
        {
            foreach (var value in JsonSerializer.Deserialize<T[]>(json) ?? [])
            {
                target.Add(value);
            }
        }
        catch (JsonException)
        {
            // Optional review material must not make the artifact unreadable.
        }
    }

    private static void TryHydrate(string json, IDictionary<string, string> target)
    {
        try
        {
            foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [])
            {
                target[pair.Key] = pair.Value;
            }
        }
        catch (JsonException)
        {
            // Optional review material must not make the artifact unreadable.
        }
    }
}

public sealed class EfSourceDocumentOwnershipReader(RavenDbContext dbContext) : ISourceDocumentOwnershipReader
{
    public async Task<IReadOnlySet<Guid>> GetOwnedSourceDocumentIdsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceDocumentIds,
        CancellationToken cancellationToken = default)
    {
        if (sourceDocumentIds.Count == 0) return new HashSet<Guid>();
        return (await dbContext.SourceDocuments.AsNoTracking()
            .Where(source => source.CompanyId == companyId && sourceDocumentIds.Contains(source.Id))
            .Select(source => source.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();
    }
}
