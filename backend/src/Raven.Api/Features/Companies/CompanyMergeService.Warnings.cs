using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.DeepResearch;
using Raven.Api.Features.Monitoring;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Changes;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Organization;
using Raven.Api.Features.Research.SavedArtifacts;
using Raven.Api.Features.Research.Briefings;

namespace Raven.Api.Features.Companies;

public sealed partial class CompanyMergeService
{
    private static IReadOnlyList<string> BuildWarnings(
        int reusableSources,
        int movableSources,
        bool hasCanonicalMonitoring,
        bool hasDuplicateMonitoring)
    {
        var warnings = new List<string>();
        if (reusableSources > 0)
        {
            warnings.Add($"{reusableSources} duplicate source document(s) match canonical URL/content and will be deduplicated.");
        }

        if (movableSources > 0)
        {
            warnings.Add($"{movableSources} source document(s) will be reassigned to the canonical company.");
        }

        if (hasCanonicalMonitoring && hasDuplicateMonitoring)
        {
            warnings.Add("The canonical company's monitoring setting will be retained; the duplicate setting will be discarded.");
        }
        else if (hasDuplicateMonitoring)
        {
            warnings.Add("The duplicate monitoring setting will be moved to the canonical company.");
        }

        return warnings;
    }

    private static CompanyResponse ToResponse(Company company) =>
        new(
            company.Id,
            company.Name,
            company.Website,
            company.Country,
            company.CreatedAt,
            company.UpdatedAt,
            company.LegalName,
            company.RegistrationNumber,
            company.Headquarters,
            company.LastResearchedAt,
            company.ArchivedAt);
}
