using Raven.Api.Features.Companies;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.Intelligence;

namespace Raven.Api.Features.Research.Planning;

/// <summary>
/// Produces a small, deterministic query set for evidence gaps. It is deliberately
/// separate from provider execution and remains the safe fallback if optional AI
/// query expansion is unavailable.
/// </summary>
public sealed class TargetedQueryPlanner
{
    public const int MaximumQueries = 15;
    public const int MaximumTargetsPerRound = 4;

    public IReadOnlyList<string> Plan(ResearchIdentityInput identity, IReadOnlyCollection<ResearchTarget>? targets)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var requested = (targets ?? [])
            .Distinct()
            .Take(MaximumTargetsPerRound)
            .ToArray();
        if (requested.Length == 0)
        {
            return [];
        }

        var name = Quote(identity.LegalName ?? identity.Name);
        var country = string.IsNullOrWhiteSpace(identity.Country) ? null : Quote(identity.Country);
        var host = CompanyIdentityNormalizer.NormalizeWebsiteHost(identity.Website);
        var queries = new List<string>();
        foreach (var target in requested)
        {
            queries.AddRange(QueriesFor(target, name, country, host, identity.RegistrationNumber));
        }

        return queries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumQueries)
            .ToArray();
    }

    private static IEnumerable<string> QueriesFor(
        ResearchTarget target,
        string name,
        string? country,
        string? officialHost,
        string? registrationNumber)
    {
        var companyAndCountry = string.Join(' ', new[] { name, country }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var site = string.IsNullOrWhiteSpace(officialHost) ? null : $"site:{officialHost}";
        return target switch
        {
            ResearchTarget.Leadership => [
                $"{companyAndCountry} CEO", $"{companyAndCountry} general director", $"{companyAndCountry} leadership",
                JoinSite(site, "leadership"), JoinSite(site, "management"), JoinSite(site, "board")],
            ResearchTarget.TaxRegistration or ResearchTarget.LegalIdentity => [
                $"{companyAndCountry} mã số thuế", $"{companyAndCountry} tax code", $"site:masothue.com {name}",
                string.IsNullOrWhiteSpace(registrationNumber) ? string.Empty : $"site:masothue.com {Quote(registrationNumber)}"],
            ResearchTarget.Markets => [
                $"{companyAndCountry} markets customers countries", $"{companyAndCountry} international customers",
                JoinSite(site, "markets"), JoinSite(site, "customers"), JoinSite(site, "global")],
            ResearchTarget.ProductsServices => [
                $"{companyAndCountry} products services", JoinSite(site, "products"),
                JoinSite(site, "solutions"), JoinSite(site, "services")],
            ResearchTarget.EmployeeScale => [
                $"{companyAndCountry} employees", $"site:topcv.vn/cong-ty {name}", $"site:linkedin.com/company {name}"],
            ResearchTarget.FoundedHistory => [
                $"{companyAndCountry} founded established", JoinSite(site, "history"), JoinSite(site, "about")],
            ResearchTarget.Locations => [
                JoinSite(site, "locations"), JoinSite(site, "offices"), JoinSite(site, "contact"), $"{companyAndCountry} offices"],
            ResearchTarget.Industry => [
                $"{companyAndCountry} industry business activities", $"site:masothue.com {name}", JoinSite(site, "about")],
            _ => []
        };
    }

    private static string JoinSite(string? site, string term) => string.IsNullOrWhiteSpace(site) ? string.Empty : $"{site} {term}";
    private static string Quote(string value) => $"\"{value.Trim().Replace("\"", " ", StringComparison.Ordinal)}\"";
}
