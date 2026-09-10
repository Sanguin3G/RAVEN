namespace Raven.Api.Features.Research.Sources;

/// <summary>
/// Profile fields for which source authority can differ. This is intentionally a
/// policy input rather than a global source score: a registry is authoritative for
/// legal identity, while the official website is usually better for products.
/// </summary>
public enum SourceField
{
    LegalIdentity,
    ProductsServices,
    EmployeeScale,
    Leadership,
    RegisteredAddress,
    OperatingLocations
}

public interface ISourceAuthorityPolicy
{
    int GetRank(SourceField field, SourceKind sourceKind);

    int Compare(SourceField field, SourceKind left, SourceKind right);

    bool IsPreferred(SourceField field, SourceKind candidate, SourceKind incumbent);
}

/// <summary>
/// A small, explainable field-specific ordering used to prioritize evidence. The
/// numbers are policy ranks, not confidence or truth probabilities, and are kept
/// behind this abstraction so they can evolve with source coverage.
/// </summary>
public sealed class SourceAuthorityPolicy : ISourceAuthorityPolicy
{
    private static readonly IReadOnlyDictionary<SourceField, IReadOnlyDictionary<SourceKind, int>> Ranks =
        new Dictionary<SourceField, IReadOnlyDictionary<SourceKind, int>>
        {
            [SourceField.LegalIdentity] = RanksFor(
                (SourceKind.BusinessRegistry, 100),
                (SourceKind.OfficialDocument, 90),
                (SourceKind.OfficialWebsite, 80),
                (SourceKind.TopCv, 50),
                (SourceKind.LinkedIn, 40),
                (SourceKind.News, 30),
                (SourceKind.ExternalWebsite, 20),
                (SourceKind.SearchResult, 10)),
            [SourceField.ProductsServices] = RanksFor(
                (SourceKind.OfficialWebsite, 100),
                (SourceKind.OfficialDocument, 95),
                (SourceKind.News, 70),
                (SourceKind.TopCv, 60),
                (SourceKind.LinkedIn, 55),
                (SourceKind.ExternalWebsite, 50),
                (SourceKind.BusinessRegistry, 30),
                (SourceKind.SearchResult, 10)),
            [SourceField.EmployeeScale] = RanksFor(
                (SourceKind.OfficialDocument, 100),
                (SourceKind.OfficialWebsite, 85),
                (SourceKind.TopCv, 80),
                (SourceKind.LinkedIn, 70),
                (SourceKind.News, 60),
                (SourceKind.ExternalWebsite, 50),
                (SourceKind.BusinessRegistry, 30),
                (SourceKind.SearchResult, 10)),
            [SourceField.Leadership] = RanksFor(
                (SourceKind.OfficialWebsite, 100),
                (SourceKind.OfficialDocument, 95),
                (SourceKind.News, 80),
                (SourceKind.LinkedIn, 70),
                (SourceKind.ExternalWebsite, 50),
                (SourceKind.TopCv, 40),
                (SourceKind.BusinessRegistry, 30),
                (SourceKind.SearchResult, 10)),
            [SourceField.RegisteredAddress] = RanksFor(
                (SourceKind.BusinessRegistry, 100),
                (SourceKind.OfficialDocument, 90),
                (SourceKind.OfficialWebsite, 80),
                (SourceKind.TopCv, 70),
                (SourceKind.LinkedIn, 50),
                (SourceKind.News, 40),
                (SourceKind.ExternalWebsite, 30),
                (SourceKind.SearchResult, 10)),
            [SourceField.OperatingLocations] = RanksFor(
                (SourceKind.OfficialWebsite, 100),
                (SourceKind.OfficialDocument, 90),
                (SourceKind.TopCv, 70),
                (SourceKind.LinkedIn, 60),
                (SourceKind.News, 50),
                (SourceKind.ExternalWebsite, 40),
                (SourceKind.BusinessRegistry, 30),
                (SourceKind.SearchResult, 10))
        };

    public int GetRank(SourceField field, SourceKind sourceKind) =>
        Ranks.TryGetValue(field, out var fieldRanks) && fieldRanks.TryGetValue(sourceKind, out var rank)
            ? rank
            : 0;

    public int Compare(SourceField field, SourceKind left, SourceKind right) =>
        GetRank(field, left).CompareTo(GetRank(field, right));

    public bool IsPreferred(SourceField field, SourceKind candidate, SourceKind incumbent) =>
        Compare(field, candidate, incumbent) > 0;

    private static IReadOnlyDictionary<SourceKind, int> RanksFor(
        params (SourceKind Kind, int Rank)[] values) =>
        values.ToDictionary(value => value.Kind, value => value.Rank);
}
