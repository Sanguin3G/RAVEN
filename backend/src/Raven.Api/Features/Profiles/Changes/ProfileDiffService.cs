using System.Text.Json;
using Raven.Api.Features.Profiles;

namespace Raven.Api.Features.Profiles.Changes;

public interface IProfileDiffService
{
    IReadOnlyList<ProfileChange> Compare(
        CompanyProfileVersion previous,
        CompanyProfileVersion current,
        DateTimeOffset? detectedAt = null);
}

/// <summary>
/// Compares accepted profile snapshots without involving an LLM or persistence.
/// Scalar values are compared by their canonical profile field path. Collection
/// values are matched by stable semantic keys so reordering does not create noise.
/// </summary>
public sealed class ProfileDiffService : IProfileDiffService
{
    private static readonly IReadOnlyList<ScalarField> ScalarFields =
    [
        new("displayName", profile => profile.DisplayName),
        new("legalName", profile => profile.LegalName),
        new("website", profile => profile.Website),
        new("country", profile => profile.Country),
        new("headquarters", profile => profile.Headquarters),
        new("registrationNumberOrTaxId", profile => profile.RegistrationNumberOrTaxId),
        new("foundedYear", profile => profile.FoundedYear),
        new("primaryIndustry", profile => profile.PrimaryIndustry),
        new("companySize", profile => profile.CompanySize),
        new("employeeCount", profile => profile.EmployeeCount),
        new("employeeCountRange", profile => profile.EmployeeCountRange),
        new("summary", profile => profile.Summary)
    ];

    private static readonly IReadOnlyList<CollectionField> CollectionFields =
    [
        new(
            "secondaryIndustries",
            profile => profile.SecondaryIndustries.Select(value => new CollectionValue(value, value))),
        new(
            "productsServices",
            profile => profile.ProductsServices.Select(value => new CollectionValue(value.Name, value))),
        new(
            "markets",
            profile => profile.Markets.Select(value => new CollectionValue(value.Name, value))),
        new(
            "leadership",
            profile => profile.Leadership.Select(value => new CollectionValue(
                string.IsNullOrWhiteSpace(value.Title) ? value.Name : value.Title,
                value))),
        new(
            "locations",
            profile => profile.Locations.Select(value => new CollectionValue(
                string.IsNullOrWhiteSpace(value.Name) ? value.Type : value.Name,
                value))),
        new(
            "publicLinks",
            profile => profile.PublicLinks.Select(value => new CollectionValue(CanonicalUrl(value.Url), value)))
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<ProfileChange> Compare(
        CompanyProfileVersion previous,
        CompanyProfileVersion current,
        DateTimeOffset? detectedAt = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (previous.CompanyId != current.CompanyId)
        {
            throw new ArgumentException("Profile versions must belong to the same company.", nameof(current));
        }

        var timestamp = detectedAt ?? DateTimeOffset.UtcNow;
        var changes = new List<ProfileChange>();

        foreach (var field in ScalarFields)
        {
            var oldValue = field.GetValue(previous);
            var newValue = field.GetValue(current);
            if (ValuesEqual(oldValue, newValue))
            {
                continue;
            }

            changes.Add(CreateChange(
                previous,
                current,
                field.Path,
                itemKey: null,
                oldValue,
                newValue,
                timestamp));
        }

        foreach (var field in CollectionFields)
        {
            CompareCollection(previous, current, field, changes, timestamp);
        }

        return changes;
    }

    private static void CompareCollection(
        CompanyProfileVersion previous,
        CompanyProfileVersion current,
        CollectionField field,
        ICollection<ProfileChange> changes,
        DateTimeOffset detectedAt)
    {
        var oldItems = BuildItems(field.GetValues(previous));
        var newItems = BuildItems(field.GetValues(current));

        foreach (var key in oldItems.Keys.Union(newItems.Keys, StringComparer.Ordinal)
                     .OrderBy(key => key, StringComparer.Ordinal))
        {
            var oldItem = oldItems.GetValueOrDefault(key);
            var newItem = newItems.GetValueOrDefault(key);
            if (oldItem is not null && newItem is not null && ValuesEqual(oldItem.Value, newItem.Value))
            {
                continue;
            }

            changes.Add(CreateChange(
                previous,
                current,
                field.Path,
                newItem?.DisplayKey ?? oldItem!.DisplayKey,
                oldItem?.Value,
                newItem?.Value,
                detectedAt));
        }
    }

    private static Dictionary<string, CollectionItem> BuildItems(IEnumerable<CollectionValue> values)
    {
        var result = new Dictionary<string, CollectionItem>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            var displayKey = NormalizeDisplayKey(value.Key);
            var baseKey = NormalizeKey(displayKey);
            if (baseKey.Length == 0)
            {
                baseKey = "(unnamed)";
                displayKey = "(unnamed)";
            }

            var occurrence = occurrences.TryGetValue(baseKey, out var count) ? count + 1 : 1;
            occurrences[baseKey] = occurrence;
            var key = occurrence == 1 ? baseKey : $"{baseKey}#{occurrence}";
            var visibleKey = occurrence == 1 ? displayKey : $"{displayKey} #{occurrence}";
            result[key] = new CollectionItem(visibleKey, value.Value);
        }

        return result;
    }

    private static ProfileChange CreateChange(
        CompanyProfileVersion previous,
        CompanyProfileVersion current,
        string fieldPath,
        string? itemKey,
        object? oldValue,
        object? newValue,
        DateTimeOffset detectedAt)
    {
        var changeType = oldValue is null
            ? ProfileChangeType.Added
            : newValue is null
                ? ProfileChangeType.Removed
                : ProfileChangeType.Changed;

        return new ProfileChange
        {
            CompanyId = current.CompanyId,
            OldProfileVersionId = previous.Id,
            NewProfileVersionId = current.Id,
            FieldPath = fieldPath,
            ItemKey = itemKey,
            ChangeType = changeType,
            OldValueJson = Serialize(oldValue),
            NewValueJson = Serialize(newValue),
            DetectedAt = detectedAt
        };
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, value.GetType(), JsonOptions);

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left is string leftText && right is string rightText)
        {
            return string.Equals(NormalizeValue(leftText), NormalizeValue(rightText), StringComparison.OrdinalIgnoreCase);
        }

        if (left is ProfileProductService leftProduct && right is ProfileProductService rightProduct)
        {
            return TextEqual(leftProduct.Name, rightProduct.Name) &&
                   TextEqual(leftProduct.Type, rightProduct.Type) &&
                   TextEqual(leftProduct.Description, rightProduct.Description);
        }

        if (left is ProfileMarket leftMarket && right is ProfileMarket rightMarket)
        {
            return TextEqual(leftMarket.Name, rightMarket.Name) &&
                   TextEqual(leftMarket.Type, rightMarket.Type);
        }

        if (left is ProfileLeader leftLeader && right is ProfileLeader rightLeader)
        {
            return TextEqual(leftLeader.Name, rightLeader.Name) &&
                   TextEqual(leftLeader.Title, rightLeader.Title);
        }

        if (left is ProfileLocation leftLocation && right is ProfileLocation rightLocation)
        {
            return TextEqual(leftLocation.Name, rightLocation.Name) &&
                   TextEqual(leftLocation.Address, rightLocation.Address) &&
                   TextEqual(leftLocation.Country, rightLocation.Country) &&
                   TextEqual(leftLocation.Type, rightLocation.Type);
        }

        if (left is ProfilePublicLink leftLink && right is ProfilePublicLink rightLink)
        {
            return string.Equals(CanonicalUrl(leftLink.Url), CanonicalUrl(rightLink.Url), StringComparison.Ordinal) &&
                   TextEqual(leftLink.Kind, rightLink.Kind) &&
                   TextEqual(leftLink.Label, rightLink.Label);
        }

        return left.Equals(right);
    }

    private static bool TextEqual(string? left, string? right) =>
        string.Equals(
            left is null ? null : NormalizeValue(left),
            right is null ? null : NormalizeValue(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDisplayKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeValue(value);

    private static string NormalizeKey(string value) =>
        NormalizeValue(value).ToUpperInvariant();

    private static string NormalizeValue(string value) =>
        string.Join(' ', value.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    private static string CanonicalUrl(string value)
    {
        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return NormalizeValue(trimmed);
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        var authority = uri.Host.ToLowerInvariant();
        if (!uri.IsDefaultPort)
        {
            authority += $":{uri.Port}";
        }

        return $"{uri.Scheme.ToLowerInvariant()}://{authority}{path}{uri.Query}{uri.Fragment}";
    }

    private sealed record ScalarField(string Path, Func<CompanyProfileVersion, object?> GetValue);

    private sealed record CollectionField(
        string Path,
        Func<CompanyProfileVersion, IEnumerable<CollectionValue>> GetValues);

    private sealed record CollectionValue(string? Key, object Value);

    private sealed record CollectionItem(string DisplayKey, object Value);
}
