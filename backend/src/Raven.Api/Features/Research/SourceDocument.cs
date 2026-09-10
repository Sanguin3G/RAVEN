using Raven.Api.Features.Companies;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Features.Research;

public sealed class SourceDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public Company Company { get; init; } = null!;
    public Guid ResearchRunId { get; init; }
    public ResearchRun ResearchRun { get; init; } = null!;
    public required string Url { get; init; }
    public required string NormalizedUrl { get; init; }
    public string? Title { get; init; }
    public string? SourceDomain { get; init; }
    public SourceKind SourceKind { get; init; } = SourceKind.ExternalWebsite;
    public string? IconUrl { get; init; }
    public string? StructuredFactsJson { get; init; }
    public DateTimeOffset RetrievedAt { get; init; }
    public required string Content { get; init; }
    public required string ContentHash { get; init; }
    public required string CrawlerProvider { get; init; }
}
