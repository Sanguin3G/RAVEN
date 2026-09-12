using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raven.Api.Features.Research.Identity;

/// <summary>
/// The bounded identity target carried forward into a research run.
/// 
/// Values that originate only from model knowledge remain identity/search
/// hints. They are not accepted Company Profile facts or evidence.
/// </summary>
public sealed record ResolvedIdentitySnapshot(
    string DisplayName,
    string? Country = null,
    string? Region = null,
    string? LegalNameHint = null,
    string? OfficialDomainHint = null,
    IdentityEntityType EntityType = IdentityEntityType.Unknown,
    string? ParentName = null,
    IdentityResolutionMethod ResolutionMethod = IdentityResolutionMethod.ModelKnowledge)
{
    public const int MaximumSerializedLength = 4_000;
    public const int MaximumDisplayNameLength = 300;
    public const int MaximumHintLength = 500;
    public const int MaximumDomainLength = 253;

    public static bool TryCreate(
        IdentityResolutionResponse response,
        out ResolvedIdentitySnapshot? snapshot,
        string? selectedEntityId = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        snapshot = null;

        if (response.Status != IdentityResolutionStatus.Resolved || response.Entities.Count == 0)
        {
            return false;
        }

        var entityId = selectedEntityId ?? response.RecommendedEntityId;
        var entity = response.Entities.FirstOrDefault(item =>
            string.Equals(item.TemporaryId, entityId, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            return false;
        }

        var parent = entity.ParentTemporaryId is null
            ? null
            : response.Entities.FirstOrDefault(item =>
                string.Equals(item.TemporaryId, entity.ParentTemporaryId, StringComparison.OrdinalIgnoreCase));

        snapshot = new ResolvedIdentitySnapshot(
            Bound(entity.DisplayName, MaximumDisplayNameLength) ?? entity.DisplayName!,
            Bound(entity.Country, MaximumHintLength),
            Bound(entity.Region, MaximumHintLength),
            Bound(entity.LegalName, MaximumHintLength),
            Bound(entity.OfficialDomain, MaximumDomainLength),
            entity.EntityType,
            Bound(parent?.DisplayName, MaximumDisplayNameLength),
            response.ResolutionMethod);
        return true;
    }

    private static string? Bound(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace('\0', ' ').Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}

/// <summary>Serializes run identity snapshots with a hard storage bound.</summary>
public static class ResolvedIdentitySnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    public static string? Serialize(ResolvedIdentitySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(snapshot, Options);
        if (json.Length > ResolvedIdentitySnapshot.MaximumSerializedLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snapshot),
                $"Resolved identity snapshot must be {ResolvedIdentitySnapshot.MaximumSerializedLength} characters or fewer.");
        }

        return json;
    }

    public static ResolvedIdentitySnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > ResolvedIdentitySnapshot.MaximumSerializedLength)
        {
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<ResolvedIdentitySnapshot>(json, Options);
            return string.IsNullOrWhiteSpace(snapshot?.DisplayName) ? null : snapshot;
        }
        catch (JsonException)
        {
            // A legacy or manually edited row must not make the run unreadable.
            return null;
        }
    }
}
