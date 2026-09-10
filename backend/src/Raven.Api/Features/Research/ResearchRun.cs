using Raven.Api.Features.Companies;

namespace Raven.Api.Features.Research;

public sealed class ResearchRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public Company Company { get; init; } = null!;
    public ResearchRunStatus Status { get; set; } = ResearchRunStatus.Searching;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public required string RequestedSearchProvider { get; init; }
    public string? ActualSearchProvider { get; set; }
    public required string RequestedCrawlerProvider { get; init; }
    public string? ActualCrawlerProvider { get; set; }
    public int SourcesFound { get; set; }
    public int SourcesSelected { get; set; }
    public int SourcesCrawled { get; set; }
    public string? Error { get; set; }
    public ICollection<SourceDocument> SourceDocuments { get; } = new List<SourceDocument>();
}

public enum ResearchRunStatus
{
    Searching,
    Crawling,
    Completed,
    Failed
}
