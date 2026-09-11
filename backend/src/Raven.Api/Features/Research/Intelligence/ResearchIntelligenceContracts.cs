using Raven.Api.Features.Companies;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Features.Research.Intelligence;

public enum GroundingMode
{
    Auto,
    Always,
    Off
}

public enum GroundedEntityType
{
    ParentGroup,
    Company,
    Subsidiary,
    Affiliate,
    Brand,
    Unknown
}

public enum GroundingConfidence
{
    Low,
    Medium,
    High
}

public enum EntityRelationship
{
    SameEntity,
    Parent,
    Subsidiary,
    Affiliate,
    DifferentEntity,
    Uncertain
}

public enum CandidateRelevance
{
    High,
    Medium,
    Low
}

public sealed record ResearchIdentityInput(
    string Name,
    string? LegalName,
    string? Website,
    string? Country,
    string? RegistrationNumber,
    string? Headquarters,
    string? ResearchHint)
{
    public static ResearchIdentityInput FromCompany(Company company, string? researchHint) => new(
        company.Name,
        company.LegalName,
        company.Website,
        company.Country,
        company.RegistrationNumber,
        company.Headquarters,
        researchHint);
}

public sealed record GroundingSourceCandidate(
    Guid CandidateId,
    string Url,
    string Domain,
    string? Title,
    string? Snippet,
    SourceKind SourceKind,
    int SearchRank,
    IReadOnlyList<string> RecommendationReasons,
    bool OfficialDomain);

public sealed record ResolvedResearchEntity(
    string TemporaryId,
    string DisplayName,
    string? LegalName,
    string? Country,
    string? Website,
    string? OfficialDomain,
    GroundedEntityType EntityType,
    string? RelationshipHint,
    GroundingConfidence Confidence,
    string? Rationale,
    IReadOnlyList<Guid> SupportingCandidateIds,
    bool Recommended);

public sealed record IdentityResolutionRequest(
    ResearchIdentityInput Identity,
    IReadOnlyList<GroundingSourceCandidate> Candidates);

public sealed record IdentityResolutionResult(
    bool Ambiguous,
    string? RecommendedTemporaryId,
    IReadOnlyList<ResolvedResearchEntity> Entities,
    string? Warning = null,
    AiFailure? Failure = null);

public interface ICompanyIdentityResolver
{
    Task<IdentityResolutionResult> ResolveAsync(
        IdentityResolutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record SourceSemanticCandidate(
    Guid CandidateId,
    string Url,
    string Domain,
    string? Title,
    string? Snippet,
    SourceKind SourceKind,
    int DeterministicScore,
    IReadOnlyList<string> DeterministicReasons);

public sealed record SourceSemanticAssessment(
    Guid CandidateId,
    EntityRelationship EntityRelationship,
    CandidateRelevance Relevance,
    bool Recommended,
    IReadOnlyList<string> Purposes,
    string? Rationale);

public sealed record SourceSemanticRerankRequest(
    ResolvedResearchEntity Target,
    IReadOnlyList<SourceSemanticCandidate> Candidates);

public sealed record SourceSemanticRerankResult(
    IReadOnlyList<SourceSemanticAssessment> Assessments,
    string? Warning = null,
    AiFailure? Failure = null);

public interface ISourceSemanticReranker
{
    Task<SourceSemanticRerankResult> RerankAsync(
        SourceSemanticRerankRequest request,
        CancellationToken cancellationToken = default);
}
