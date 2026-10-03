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
    private async Task RewriteMovedProfilePayloadsAsync(
        Guid canonicalCompanyId,
        IReadOnlyCollection<Guid> movedProfileCandidateIds,
        ProfileVersionMergePlan profileVersionMerge,
        CancellationToken cancellationToken)
    {
        if (movedProfileCandidateIds.Count > 0)
        {
            var candidates = await dbContext.CompanyProfileCandidates
                .Where(profile => movedProfileCandidateIds.Contains(profile.Id))
                .ToListAsync(cancellationToken);
            foreach (var candidate in candidates)
            {
                var rewritten = RewritePayloadReference(candidate.CandidateJson, "companyId", canonicalCompanyId);
                if (rewritten is not null)
                {
                    candidate.CandidateJson = rewritten;
                }
            }
        }

        if (profileVersionMerge.AssignedVersions.Count == 0)
        {
            return;
        }

        var profiles = await dbContext.CompanyProfileVersions
            .Where(profile => profileVersionMerge.AssignedVersions.Keys.Contains(profile.Id))
            .ToListAsync(cancellationToken);
        foreach (var profile in profiles)
        {
            var rewritten = profileVersionMerge.MovedProfileIds.Contains(profile.Id)
                ? RewritePayloadReference(profile.ProfileJson, "companyId", canonicalCompanyId)
                : null;
            rewritten = RewritePayloadReference(
                rewritten ?? profile.ProfileJson,
                "version",
                profileVersionMerge.AssignedVersions[profile.Id]);
            if (rewritten is not null)
            {
                profile.ProfileJson = rewritten;
            }
        }
    }

    private static string? RewritePayloadReference<TValue>(
        string? json,
        string propertyName,
        TValue value)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject payload)
            {
                return null;
            }

            var property = payload.FirstOrDefault(item =>
                string.Equals(item.Key, propertyName, StringComparison.OrdinalIgnoreCase)).Key;
            payload[property ?? propertyName] = JsonValue.Create(value);
            return payload.ToJsonString();
        }
        catch (JsonException)
        {
            // Keep the original payload intact. Hydration already rejects a
            // malformed snapshot, and replacing it would lose user evidence.
            return null;
        }
    }

    private static string? PreferCanonicalValue(string? canonicalValue, string? duplicateValue) =>
        !string.IsNullOrWhiteSpace(canonicalValue) ? canonicalValue : duplicateValue;
}
