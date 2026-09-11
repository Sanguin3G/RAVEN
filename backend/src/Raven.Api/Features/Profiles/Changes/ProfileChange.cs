namespace Raven.Api.Features.Profiles.Changes;

/// <summary>
/// The kind of change detected between two accepted profile snapshots.
/// </summary>
public enum ProfileChangeType
{
    Added,
    Removed,
    Changed
}

/// <summary>
/// A deterministic, reviewable difference between two company profile versions.
/// Collection changes use <see cref="ItemKey"/> to identify the logical item
/// instead of relying on the item's position in a list.
/// </summary>
public sealed class ProfileChange
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public Guid OldProfileVersionId { get; init; }
    public Guid NewProfileVersionId { get; init; }
    public required string FieldPath { get; init; }
    public string? ItemKey { get; init; }
    public required ProfileChangeType ChangeType { get; init; }
    public string? OldValueJson { get; init; }
    public string? NewValueJson { get; init; }
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;
}
