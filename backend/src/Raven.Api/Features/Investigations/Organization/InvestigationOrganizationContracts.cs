using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Research.SavedArtifacts;

namespace Raven.Api.Features.Research.Organization;

/// <summary>
/// A derived, versioned view of one saved investigation. The source artifact
/// remains immutable and is always the source of truth for re-organization.
/// </summary>
public sealed class InvestigationOrganizationRevision
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SavedResearchArtifactId { get; init; }
    public Guid CompanyId { get; init; }
    public int Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public required string ExecutiveSummary { get; init; }
    public required string ThemesJson { get; init; }
    public required string EvidenceGapsJson { get; init; }
    public required string SuggestedFollowUpsJson { get; init; }
    public required string UncertaintiesJson { get; init; }
    public bool IsHumanEdited { get; init; }
}

public sealed record InvestigationOrganizationTheme(
    string Name,
    string Summary,
    int ClaimCount);

public sealed record InvestigationOrganizationResponse(
    Guid Id,
    Guid SavedResearchArtifactId,
    int Version,
    DateTimeOffset CreatedAt,
    string ExecutiveSummary,
    IReadOnlyList<InvestigationOrganizationTheme> Themes,
    IReadOnlyList<string> EvidenceGaps,
    IReadOnlyList<string> SuggestedFollowUps,
    IReadOnlyList<string> Uncertainties,
    bool IsHumanEdited);

public interface IInvestigationOrganizationService
{
    Task<InvestigationOrganizationResponse?> GetCurrentAsync(
        Guid companyId,
        Guid artifactId,
        CancellationToken cancellationToken = default);

    Task<InvestigationOrganizationResponse?> OrganizeAsync(
        Guid companyId,
        Guid artifactId,
        CancellationToken cancellationToken = default);
}

public sealed class InvestigationOrganizationService(
    RavenDbContext db,
    ISavedResearchArtifactService artifacts) : IInvestigationOrganizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InvestigationOrganizationResponse?> GetCurrentAsync(
        Guid companyId,
        Guid artifactId,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(companyId, artifactId);
        var revision = await db.Set<InvestigationOrganizationRevision>()
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.SavedResearchArtifactId == artifactId)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return revision is null ? null : ToResponse(revision);
    }

    public async Task<InvestigationOrganizationResponse?> OrganizeAsync(
        Guid companyId,
        Guid artifactId,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(companyId, artifactId);
        var artifact = await artifacts.GetAsync(companyId, artifactId, cancellationToken);
        if (artifact is null)
        {
            return null;
        }

        var previousVersion = await db.Set<InvestigationOrganizationRevision>()
            .Where(item => item.CompanyId == companyId && item.SavedResearchArtifactId == artifactId)
            .Select(item => (int?)item.Version)
            .MaxAsync(cancellationToken) ?? 0;

        var themes = artifact.Claims
            .GroupBy(claim => string.IsNullOrWhiteSpace(claim.Field) ? "Uncategorized" : claim.Field.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .Select(group => new InvestigationOrganizationTheme(
                group.Key,
                string.Join(" ", group.Select(claim => claim.Statement.Trim()).Where(statement => statement.Length > 0).Take(3)),
                group.Count()))
            .ToArray();

        var evidenceGaps = artifact.Claims
            .Where(claim => claim.SupportingSourceLeadIds is null || claim.SupportingSourceLeadIds.Count == 0)
            .Select(claim => $"{claim.Field}: {claim.Statement}")
            .Concat(artifact.Uncertainties.Select(uncertainty => $"Resolve uncertainty: {uncertainty}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();

        var followUps = evidenceGaps
            .Select(gap => gap.StartsWith("Resolve uncertainty:", StringComparison.OrdinalIgnoreCase)
                ? gap
                : $"Find an authoritative source for {gap}")
            .Take(100)
            .ToArray();

        var revision = new InvestigationOrganizationRevision
        {
            SavedResearchArtifactId = artifact.Id,
            CompanyId = companyId,
            Version = previousVersion + 1,
            CreatedAt = DateTimeOffset.UtcNow,
            ExecutiveSummary = artifact.Summary,
            ThemesJson = JsonSerializer.Serialize(themes, JsonOptions),
            EvidenceGapsJson = JsonSerializer.Serialize(evidenceGaps, JsonOptions),
            SuggestedFollowUpsJson = JsonSerializer.Serialize(followUps, JsonOptions),
            UncertaintiesJson = JsonSerializer.Serialize(artifact.Uncertainties, JsonOptions),
            IsHumanEdited = false
        };

        db.Set<InvestigationOrganizationRevision>().Add(revision);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(revision);
    }

    private static InvestigationOrganizationResponse ToResponse(InvestigationOrganizationRevision revision) =>
        new(
            revision.Id,
            revision.SavedResearchArtifactId,
            revision.Version,
            revision.CreatedAt,
            revision.ExecutiveSummary,
            Deserialize<InvestigationOrganizationTheme>(revision.ThemesJson),
            Deserialize<string>(revision.EvidenceGapsJson),
            Deserialize<string>(revision.SuggestedFollowUpsJson),
            Deserialize<string>(revision.UncertaintiesJson),
            revision.IsHumanEdited);

    private static IReadOnlyList<T> Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void ValidateIds(Guid companyId, Guid artifactId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company ID is required.", nameof(companyId));
        if (artifactId == Guid.Empty) throw new ArgumentException("An investigation ID is required.", nameof(artifactId));
    }
}
