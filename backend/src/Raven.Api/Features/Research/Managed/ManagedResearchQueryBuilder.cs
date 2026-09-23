using System.Text;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>
/// Builds a bounded, provider-neutral research query from the resolved company
/// identity, accepted profile context, and known evidence gaps.
/// </summary>
public static class ManagedResearchQueryBuilder
{
    public static string Build(ManagedResearchCompanyContext context, string objective)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.DisplayName))
        {
            throw new ArgumentException("A company display name is required.", nameof(context));
        }

        var question = ManagedResearchQuestionValidation.NormalizeRequired(objective, nameof(objective));
        var builder = new StringBuilder();
        builder.AppendLine("Research the following company using current public sources.");
        builder.AppendLine("Treat the identity and profile context below as hints to disambiguate the entity, not as evidence.");
        builder.AppendLine();
        builder.AppendLine("Company identity:");
        Append(builder, "Display name", context.DisplayName, 500);
        Append(builder, "Legal name", context.LegalName, 500);
        Append(builder, "Official website", context.OfficialWebsite, 500);
        Append(builder, "Country", context.Country, 300);
        Append(builder, "Headquarters", context.Headquarters, 500);

        if (!string.IsNullOrWhiteSpace(context.AcceptedProfileSummary))
        {
            builder.AppendLine("Accepted profile context (do not assume unsupported details):");
            builder.AppendLine(Bound(context.AcceptedProfileSummary, 3_000));
        }

        var gaps = context.EvidenceGaps
            .Where(gap => !string.IsNullOrWhiteSpace(gap))
            .Select(gap => Bound(gap, 300))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
        if (gaps.Length > 0)
        {
            builder.AppendLine("Known evidence gaps to prioritize:");
            foreach (var gap in gaps)
            {
                builder.Append("- ").AppendLine(gap);
            }
        }

        builder.AppendLine();
        builder.AppendLine("Research question:");
        builder.AppendLine(question);
        builder.AppendLine();
        builder.AppendLine("Use authoritative public sources where possible. Return a concise summary, reviewable claims, source URLs, and uncertainties. Do not invent missing facts.");

        var query = builder.ToString().Trim();
        return query.Length <= ManagedResearchLimits.MaxQueryLength
            ? query
            : query[..ManagedResearchLimits.MaxQueryLength].TrimEnd();
    }

    private static void Append(StringBuilder builder, string label, string? value, int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(label).Append(": ").AppendLine(Bound(value, maxLength));
        }
    }

    private static string Bound(string value, int maxLength)
    {
        var normalized = value.Replace('\0', ' ').Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd();
    }
}
