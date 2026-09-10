using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Features.Research;

/// <summary>
/// A persisted, reviewable discovery result. Candidates are server-owned so an
/// acquisition request can only select URLs that were actually discovered for
/// the research run.
/// </summary>
public sealed class ResearchCandidate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ResearchRunId { get; init; }
    public ResearchRun ResearchRun { get; init; } = null!;
    public required string Url { get; init; }
    public required string NormalizedUrl { get; init; }
    public required string Domain { get; init; }
    public string? Title { get; init; }
    public string? Snippet { get; init; }
    public SourceKind SourceKind { get; init; }
    public int SearchRank { get; init; }
    // Kept internal to ranking; the UI receives recommendation reasons instead.
    public int Score { get; init; }
    public string? RecommendationReasonsJson { get; init; }
    public bool Recommended { get; init; }
    public bool Selected { get; set; }
    public CandidateAcquisitionStatus AcquisitionStatus { get; set; } = CandidateAcquisitionStatus.Pending;
    public string? AcquisitionError { get; set; }
    public string? IconUrl { get; init; }
    public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum CandidateAcquisitionStatus
{
    Pending,
    Acquiring,
    Acquired,
    Failed,
    DuplicateSkipped,
    Unavailable
}
