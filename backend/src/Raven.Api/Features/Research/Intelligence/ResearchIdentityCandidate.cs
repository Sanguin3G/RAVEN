using Raven.Api.Features.Research;

namespace Raven.Api.Features.Research.Intelligence;

/// <summary>
/// A possible real-world organization returned by the identity-grounding step.
/// This is research context, not an accepted Company or profile fact.
/// </summary>
public sealed class ResearchIdentityCandidate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ResearchRunId { get; init; }
    public required string TemporaryId { get; init; }
    public required string DisplayName { get; init; }
    public string? LegalName { get; init; }
    public string? Country { get; init; }
    public string? Website { get; init; }
    public string? OfficialDomain { get; init; }
    public GroundedEntityType EntityType { get; init; }
    public string? RelationshipHint { get; init; }
    public GroundingConfidence Confidence { get; init; }
    public string? Rationale { get; init; }
    public string SupportingCandidateIdsJson { get; init; } = "[]";
    public bool Recommended { get; init; }
    public bool Selected { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
