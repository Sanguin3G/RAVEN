namespace Raven.Api.Features.Companies.Workspace;

/// <summary>
/// Durable acknowledgement state for the workspace research queue. It hides a
/// review group through a timestamp; newer research for the same group is
/// therefore visible again without deleting any research history.
/// </summary>
public sealed class WorkspaceResearchReviewState
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ReviewKey { get; init; }
    public Guid CompanyId { get; init; }
    public required string Method { get; init; }
    public required string TopicKey { get; init; }
    public DateTimeOffset AcknowledgedThrough { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record WorkspaceResearchReviewAcknowledgement(
    string ReviewKey,
    DateTimeOffset AcknowledgedThrough);

public sealed record WorkspaceResearchReviewAcknowledgeRequest(
    IReadOnlyCollection<WorkspaceResearchReviewAcknowledgement>? Items = null);

public sealed record WorkspaceResearchReviewCleanupCandidate(
    string ReviewKey,
    Guid CompanyId,
    string CompanyName,
    string Method,
    string Title,
    string Reason,
    int OccurrenceCount,
    DateTimeOffset AcknowledgedThrough);

public sealed record WorkspaceResearchReviewCommandResponse(
    int AcknowledgedCount,
    int HiddenOccurrenceCount,
    int SkippedCount);
