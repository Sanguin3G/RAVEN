using System.Text;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Research.Coverage;

namespace Raven.Api.Features.Research.ExternalImport;

/// <summary>
/// Builds a focused, human-readable brief for ChatGPT, Claude, Gemini,
/// Perplexity, or another external assistant. It does not call an assistant.
/// </summary>
public sealed class ExternalResearchBriefGenerator : IExternalResearchBriefGenerator
{
    private static readonly ResearchTarget[] AllTargets = Enum.GetValues<ResearchTarget>();

    public ExternalResearchBrief Generate(ExternalResearchBriefRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Company);

        var objective = NormalizeObjective(request.ResearchObjective);
        var focusTargets = DetermineFocusTargets(request);
        var builder = new StringBuilder();

        builder.AppendLine("You are helping prepare research notes for RAVEN, a company intelligence workspace.");
        builder.AppendLine("Research only the company identified below. Do not merge it with a similarly named parent, subsidiary, or unrelated company.");
        builder.AppendLine("Use current, publicly accessible sources. Prefer official company, government, regulatory, and clearly attributable sources.");
        builder.AppendLine("Do not guess. Mark anything unsupported or ambiguous under Uncertainties, and include the exact supporting URL for each claim.");
        builder.AppendLine();
        builder.AppendLine("## Research Objective");
        builder.AppendLine(objective);
        builder.AppendLine();
        builder.AppendLine("## Company Identity");
        AppendIdentity(builder, request.Company);
        builder.AppendLine();

        builder.AppendLine("## Current RAVEN Context (research gaps, not facts to repeat blindly)");
        AppendProfile(builder, request.Profile);
        AppendCoverage(builder, request.Coverage, focusTargets);
        builder.AppendLine();

        builder.AppendLine("## Focus");
        if (focusTargets.Count == 0)
        {
            builder.AppendLine("Address the stated objective directly and keep the investigation narrow.");
        }
        else
        {
            foreach (var target in focusTargets)
            {
                builder.Append("- ").AppendLine(TargetDescription(target));
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Required Response Format");
        builder.AppendLine("Research Summary");
        builder.AppendLine("<2–5 concise paragraphs>");
        builder.AppendLine();
        builder.AppendLine("Claims");
        builder.AppendLine("- Field: <RAVEN topic>");
        builder.AppendLine("  Claim: <one specific, reviewable statement>");
        builder.AppendLine("  Supporting URLs: <one or more exact https:// URLs>");
        builder.AppendLine("  Notes: <scope, date, or entity caveat>");
        builder.AppendLine();
        builder.AppendLine("Sources");
        builder.AppendLine("- Title: <page or document title>");
        builder.AppendLine("  URL: <exact https:// URL>");
        builder.AppendLine("  Publisher: <publisher or domain>");
        builder.AppendLine("  Date: <published/updated date, if known>");
        builder.AppendLine("  Supports: <claim fields supported>");
        builder.AppendLine();
        builder.AppendLine("Uncertainties");
        builder.AppendLine("- <unknown, conflicting, stale, or ambiguous point>");
        builder.AppendLine();
        builder.AppendLine("Suggested Follow-up");
        builder.AppendLine("- <one narrowly scoped next step, if needed>");
        builder.AppendLine();
        builder.AppendLine("Return research notes only. RAVEN will review claims and acquire useful URLs separately; your response is not accepted profile evidence by itself.");

        return new ExternalResearchBrief(objective, focusTargets, builder.ToString());
    }

    private static string NormalizeObjective(string? objective) =>
        string.IsNullOrWhiteSpace(objective)
            ? "Improve the company profile only where current evidence is missing or weak."
            : objective.Trim();

    private static IReadOnlyList<ResearchTarget> DetermineFocusTargets(ExternalResearchBriefRequest request)
    {
        if (request.RequestedTargets is { Count: > 0 })
        {
            return request.RequestedTargets.Distinct().ToArray();
        }

        if (request.Coverage is { Items.Count: > 0 })
        {
            var gaps = request.Coverage.Items
                .Where(item => item.Level is CoverageLevel.Missing or CoverageLevel.Weak)
                .Select(item => item.Target)
                .Distinct()
                .ToArray();
            if (gaps.Length > 0)
            {
                return gaps;
            }
        }

        if (request.Profile is not null)
        {
            var gaps = AllTargets.Where(target => IsProfileGap(request.Profile, target)).ToArray();
            if (gaps.Length > 0)
            {
                return gaps;
            }
        }

        // No coverage signal means the user supplied an objective but no reason
        // to fan out into a broad, expensive investigation.
        return [];
    }

    private static void AppendIdentity(StringBuilder builder, Company company)
    {
        AppendKnown(builder, "Name", company.Name);
        AppendKnown(builder, "Legal name", company.LegalName);
        AppendKnown(builder, "Website", company.Website);
        AppendKnown(builder, "Country", company.Country);
        AppendKnown(builder, "Headquarters", company.Headquarters);
        AppendKnown(builder, "Registration/tax ID", company.RegistrationNumber);
    }

    private static void AppendProfile(StringBuilder builder, CompanyProfileSnapshot? profile)
    {
        if (profile is null)
        {
            builder.AppendLine("- No accepted profile context was supplied.");
            return;
        }

        AppendKnown(builder, "Accepted display name", profile.DisplayName);
        AppendKnown(builder, "Accepted legal name", profile.LegalName);
        AppendKnown(builder, "Accepted website", profile.Website);
        AppendKnown(builder, "Accepted country", profile.Country);
        AppendKnown(builder, "Accepted headquarters", profile.Headquarters);
        AppendKnown(builder, "Founded year", profile.FoundedYear?.ToString());
        AppendKnown(builder, "Primary industry", profile.PrimaryIndustry);
        AppendKnown(builder, "Company size", profile.CompanySize);
        AppendKnown(builder, "Employee count", profile.EmployeeCount?.ToString());
        AppendKnown(builder, "Employee count range", profile.EmployeeCountRange);
        AppendKnown(builder, "Accepted summary", profile.Summary);

        AppendCollection(builder, "Products/services", profile.ProductsServices.Select(item => item.Name));
        AppendCollection(builder, "Markets", profile.Markets.Select(item => item.Name));
        AppendCollection(builder, "Leadership", profile.Leadership.Select(item => item.Name));
        AppendCollection(builder, "Locations", profile.Locations.Select(item => item.Address ?? item.Name));
    }

    private static void AppendCoverage(
        StringBuilder builder,
        EvidenceCoverageResponse? coverage,
        IReadOnlyCollection<ResearchTarget> focusTargets)
    {
        if (coverage is null)
        {
            builder.AppendLine("- No evidence coverage assessment was supplied.");
            return;
        }

        var items = coverage.Items
            .Where(item => focusTargets.Count == 0 || focusTargets.Contains(item.Target))
            .ToArray();
        if (items.Length == 0)
        {
            builder.AppendLine("- No matching coverage items were supplied.");
            return;
        }

        foreach (var item in items)
        {
            builder.Append("- ").Append(item.Target).Append(": ")
                .Append(item.Level).Append(" (existing evidence sources: ")
                .Append(item.SupportingSourceCount).AppendLine(")");
        }
    }

    private static bool IsProfileGap(CompanyProfileSnapshot profile, ResearchTarget target) => target switch
    {
        ResearchTarget.LegalIdentity => string.IsNullOrWhiteSpace(profile.LegalName),
        ResearchTarget.TaxRegistration => string.IsNullOrWhiteSpace(profile.RegistrationNumberOrTaxId),
        ResearchTarget.FoundedHistory => profile.FoundedYear is null,
        ResearchTarget.Industry => string.IsNullOrWhiteSpace(profile.PrimaryIndustry),
        ResearchTarget.EmployeeScale => string.IsNullOrWhiteSpace(profile.CompanySize) && profile.EmployeeCount is null && string.IsNullOrWhiteSpace(profile.EmployeeCountRange),
        ResearchTarget.ProductsServices => profile.ProductsServices.Count == 0,
        ResearchTarget.Markets => profile.Markets.Count == 0,
        ResearchTarget.Leadership => profile.Leadership.Count == 0,
        ResearchTarget.Locations => profile.Locations.Count == 0 && string.IsNullOrWhiteSpace(profile.Headquarters),
        _ => true
    };

    private static string TargetDescription(ResearchTarget target) => target switch
    {
        ResearchTarget.LegalIdentity => "Legal identity: confirm the legal entity name and relationship to the named company.",
        ResearchTarget.TaxRegistration => "Tax/registration: find an attributable public registration identifier or state that it is unavailable.",
        ResearchTarget.FoundedHistory => "Founded history: establish the founding year/date and cite the source.",
        ResearchTarget.Industry => "Industry: describe the primary industry using the company's own or authoritative classification.",
        ResearchTarget.EmployeeScale => "Employee scale: find a current range or explicitly dated count; do not infer precision.",
        ResearchTarget.ProductsServices => "Products/services: identify marketed offerings and distinguish them from registered business activities.",
        ResearchTarget.Markets => "Markets: identify served geographies, customer segments, or market verticals with dates where relevant.",
        ResearchTarget.Leadership => "Leadership: identify current named leaders and roles, with the page date or retrieval context.",
        ResearchTarget.Locations => "Locations: identify headquarters and material operating locations, preserving address uncertainty.",
        _ => target.ToString()
    };

    private static void AppendKnown(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append("- ").Append(label).Append(": ").AppendLine(value.Trim());
        }
    }

    private static void AppendCollection(StringBuilder builder, string label, IEnumerable<string?> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (items.Length > 0)
        {
            builder.Append("- ").Append(label).Append(": ").AppendLine(string.Join("; ", items));
        }
    }
}
