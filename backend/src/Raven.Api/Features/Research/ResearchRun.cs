using Raven.Api.Features.Companies;
using Raven.Api.Features.Research.Intelligence;

namespace Raven.Api.Features.Research;

public sealed class ResearchRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public Company Company { get; init; } = null!;
    public ResearchRunStatus Status { get; set; } = ResearchRunStatus.Searching;
    public ResearchStage Stage { get; set; } = ResearchStage.Discovering;
    public GroundingMode GroundingMode { get; set; } = GroundingMode.Auto;
    public Guid? ResolvedIdentityCandidateId { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public required string RequestedSearchProvider { get; init; }
    public string? ActualSearchProvider { get; set; }
    public required string RequestedCrawlerProvider { get; init; }
    public string? ActualCrawlerProvider { get; set; }
    public string? ResearchHint { get; set; }
    public int QueriesTotal { get; set; }
    public int QueriesCompleted { get; set; }
    // Retained for the Day-2 response contract. This is the raw search-result count.
    public int SourcesFound { get; set; }
    public int UniqueCandidates { get; set; }
    public int RecommendedCandidates { get; set; }
    public int SourcesSelected { get; set; }
    public int CrawlTotal { get; set; }
    public int CrawlCompleted { get; set; }
    public int CrawlSucceeded { get; set; }
    public int CrawlFailed { get; set; }
    public int DocumentsAdded { get; set; }
    public int DuplicatesSkipped { get; set; }
    // Retained for compatibility: crawler responses that contained usable content.
    public int SourcesCrawled { get; set; }
    public string? Error { get; set; }
    public ICollection<SourceDocument> SourceDocuments { get; } = new List<SourceDocument>();
    public ICollection<ResearchCandidate> Candidates { get; } = new List<ResearchCandidate>();
}

public enum ResearchRunStatus
{
    Searching,
    Crawling,
    Completed,
    Failed
}

public enum ResearchStage
{
    Identifying,
    Discovering,
    Grounding,
    AwaitingIdentitySelection,
    AwaitingSourceSelection,
    Acquiring,
    EvidenceReady,
    GeneratingProfile,
    AwaitingProfileConfirmation,
    Completed,
    Failed
}
