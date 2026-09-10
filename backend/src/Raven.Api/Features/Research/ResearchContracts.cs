namespace Raven.Api.Features.Research;

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
    string? Error);

public sealed record SourceDocumentResponse(
    Guid Id,
    Guid CompanyId,
    Guid ResearchRunId,
    string Url,
    string? Title,
    string? SourceDomain,
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
    DateTimeOffset RetrievedAt,
    string Content,
    string ContentHash,
    string CrawlerProvider);
