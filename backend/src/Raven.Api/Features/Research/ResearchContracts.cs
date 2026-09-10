namespace Raven.Api.Features.Research;

using Raven.Api.Features.Research.Sources;

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
    int DuplicatesSkipped);

public sealed record DiscoverResearchRequest(string? ResearchHint = null);

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
    DateTimeOffset DiscoveredAt);

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
