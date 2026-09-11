namespace Raven.Api.Features.Research;

using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Coverage;

public sealed record ResearchRunResponse(
    Guid Id,
    Guid CompanyId,
    ResearchRunStatus Status,
    string RequestedSearchProvider,
    string? ActualSearchProvider,
    string RequestedCrawlerProvider,
    string? ActualCrawlerProvider,
    int SourcesFound,
    int SourcesSelected,
    int SourcesCrawled,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error,
    ResearchStage Stage,
    string? ResearchHint,
    int QueriesTotal,
    int QueriesCompleted,
    int UniqueCandidates,
    int RecommendedCandidates,
    int CrawlTotal,
    int CrawlCompleted,
    int CrawlSucceeded,
    int CrawlFailed,
    int DocumentsAdded,
    int DuplicatesSkipped,
    GroundingMode GroundingMode,
    Guid? ResolvedIdentityCandidateId,
    ResearchMode Mode = ResearchMode.Initial,
    Guid? BaseProfileVersionId = null,
    IReadOnlyList<ResearchTarget>? Targets = null);

public sealed record DiscoverResearchRequest(
    string? ResearchHint = null,
    GroundingMode? GroundingMode = null,
    bool UseAcceptedProfileIdentity = false,
    ResearchMode Mode = ResearchMode.Initial,
    Guid? BaseProfileVersionId = null,
    IReadOnlyList<ResearchTarget>? Targets = null);

/// <summary>Returns a persisted possible real-world research target.</summary>
public sealed record ResearchIdentityCandidateResponse(
    Guid Id,
    Guid ResearchRunId,
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
    bool Recommended,
    bool Selected,
    DateTimeOffset CreatedAt);

/// <summary>Selects one server-owned identity candidate for a research run.</summary>
public sealed record SelectResearchIdentityRequest(Guid CandidateId);

public sealed record ResearchCandidateResponse(
    Guid Id,
    Guid ResearchRunId,
    string Url,
    string NormalizedUrl,
    string Domain,
    string? Title,
    string? Snippet,
    SourceKind SourceKind,
    IReadOnlyList<string> RecommendationReasons,
    bool Recommended,
    bool Selected,
    CandidateAcquisitionStatus AcquisitionStatus,
    string? AcquisitionError,
    string? IconUrl,
    DateTimeOffset DiscoveredAt,
    EntityRelationship? EntityRelationship = null,
    CandidateRelevance? SemanticRelevance = null,
    IReadOnlyList<string>? SemanticPurposes = null,
    string? SemanticRationale = null);

public sealed record AcquireResearchCandidatesRequest(IReadOnlyList<Guid> CandidateIds);

public sealed record SourceDocumentResponse(
    Guid Id,
    Guid CompanyId,
    Guid ResearchRunId,
    string Url,
    string? Title,
    string? SourceDomain,
    SourceKind SourceKind,
    string? IconUrl,
    DateTimeOffset RetrievedAt,
    string CrawlerProvider,
    string ContentPreview);

public sealed record SourceDocumentDetailResponse(
    Guid Id,
    Guid CompanyId,
    Guid ResearchRunId,
    string Url,
    string NormalizedUrl,
    string? Title,
    string? SourceDomain,
    SourceKind SourceKind,
    string? IconUrl,
    string? StructuredFactsJson,
    DateTimeOffset RetrievedAt,
    string Content,
    string ContentHash,
    string CrawlerProvider);
