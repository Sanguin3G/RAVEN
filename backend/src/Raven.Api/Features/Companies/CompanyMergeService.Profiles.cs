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
    private async Task<ProfileVersionMergePlan> PrepareProfileVersionMergeAsync(
        Guid canonicalCompanyId,
        Guid duplicateCompanyId,
        CancellationToken cancellationToken)
    {
        var canonicalProfiles = await dbContext.CompanyProfileVersions
            .AsNoTracking()
            .Where(profile => profile.CompanyId == canonicalCompanyId)
            .OrderBy(profile => profile.Version)
            .ThenBy(profile => profile.Id)
            .Select(profile => new { profile.Id, profile.Version, profile.ConfirmedAt, profile.GeneratedAt })
            .ToListAsync(cancellationToken);
        var duplicateProfiles = await dbContext.CompanyProfileVersions
            .AsNoTracking()
            .Where(profile => profile.CompanyId == duplicateCompanyId)
            .OrderBy(profile => profile.Version)
            .ThenBy(profile => profile.Id)
            .Select(profile => new { profile.Id, profile.Version, profile.ConfirmedAt, profile.GeneratedAt })
            .ToListAsync(cancellationToken);
        if (duplicateProfiles.Count == 0)
        {
            return new ProfileVersionMergePlan(
                new Dictionary<Guid, int>(),
                new HashSet<Guid>());
        }

        // Move both sets out of the positive version range before assigning the
        // merged history. Version order follows the original accepted snapshot
        // timestamps, not which record happened to be selected as canonical, so
        // a richer/newer duplicate profile cannot be hidden by an older sparse
        // profile. Every changed version is mirrored in ProfileJson because
        // hydration validates the serialized snapshot metadata.
        var temporaryVersion = -1;
        foreach (var profile in duplicateProfiles)
        {
            await dbContext.CompanyProfileVersions
                .Where(item => item.Id == profile.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, temporaryVersion--), cancellationToken);
        }
        foreach (var profile in canonicalProfiles)
        {
            await dbContext.CompanyProfileVersions
                .Where(item => item.Id == profile.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, temporaryVersion--), cancellationToken);
        }

        var nextVersion = 1;
        var assignedVersions = new Dictionary<Guid, int>(duplicateProfiles.Count + canonicalProfiles.Count);
        foreach (var profile in canonicalProfiles
                     .Concat(duplicateProfiles)
                     .OrderBy(profile => profile.ConfirmedAt)
                     .ThenBy(profile => profile.GeneratedAt)
                     .ThenBy(profile => profile.Id))
        {
            var assignedVersion = nextVersion++;
            assignedVersions[profile.Id] = assignedVersion;
            await dbContext.CompanyProfileVersions
                .Where(item => item.Id == profile.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, assignedVersion), cancellationToken);
        }

        return new ProfileVersionMergePlan(
            assignedVersions,
            duplicateProfiles.Select(profile => profile.Id).ToHashSet());
    }

    private sealed record ProfileVersionMergePlan(
        IReadOnlyDictionary<Guid, int> AssignedVersions,
        IReadOnlySet<Guid> MovedProfileIds);
}
