using Raven.Api.Features.Research.Coverage;

namespace Raven.Api.Features.Profiles.Enrichment;

public sealed record StartTargetedResearchRequest(
    IReadOnlyList<ResearchTarget>? Targets,
    Guid? BaseProfileVersionId = null);

public sealed record ProfilePatchChange(
    string FieldPath,
    string? OldValue,
    string? ProposedValue,
    IReadOnlyList<Guid> EvidenceSourceDocumentIds);

/// <summary>A reviewable, server-owned targeted profile candidate.</summary>
public sealed record ProfilePatchCandidate(
    Guid CandidateId,
    Guid ResearchRunId,
    Guid BaseProfileVersionId,
    IReadOnlyList<ResearchTarget> AllowedTargets,
    IReadOnlyList<ProfilePatchChange> Changes,
    IReadOnlyList<string> Warnings);

public interface ITargetedProfileUpdateService
{
    Task<Research.ResearchRunResponse?> StartAsync(Guid companyId, StartTargetedResearchRequest request, CancellationToken cancellationToken);
    Task<ProfilePatchCandidate?> GenerateAsync(Guid researchRunId, CancellationToken cancellationToken);
    Task<CompanyProfileVersion?> ConfirmAsync(Guid researchRunId, Guid candidateId, CancellationToken cancellationToken);
}
