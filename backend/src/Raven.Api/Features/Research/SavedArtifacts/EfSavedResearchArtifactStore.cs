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
        var artifacts = await dbContext.SavedResearchArtifacts.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        return artifacts.Select(Hydrate).ToArray();
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

        return artifact;
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
