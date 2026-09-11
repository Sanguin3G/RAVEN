using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Profiles.Changes;

public sealed record ProfileChangeResponse(
    Guid Id,
    Guid CompanyId,
    int FromVersion,
    int ToVersion,
    string FieldPath,
    string? ItemKey,
    ProfileChangeType ChangeType,
    string? OldValueJson,
    string? NewValueJson,
    DateTimeOffset DetectedAt);

public interface IProfileChangeQueryService
{
    Task<IReadOnlyList<ProfileChangeResponse>> ListLatestAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads persisted profile differences without recalculating them at request time.</summary>
public sealed class ProfileChangeQueryService(RavenDbContext dbContext) : IProfileChangeQueryService
{
    public async Task<IReadOnlyList<ProfileChangeResponse>> ListLatestAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var latest = await dbContext.CompanyProfileVersions
            .AsNoTracking()
            .Where(profile => profile.CompanyId == companyId)
            .OrderByDescending(profile => profile.Version)
            .Select(profile => new { profile.Id, profile.Version })
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return [];
        }

        var versions = await dbContext.CompanyProfileVersions
            .AsNoTracking()
            .Where(profile => profile.CompanyId == companyId)
            .Select(profile => new { profile.Id, profile.Version })
            .ToDictionaryAsync(profile => profile.Id, profile => profile.Version, cancellationToken);

        var changes = await dbContext.ProfileChanges
            .AsNoTracking()
            .Where(change => change.CompanyId == companyId && change.NewProfileVersionId == latest.Id)
            .OrderBy(change => change.FieldPath)
            .ThenBy(change => change.ItemKey)
            .ToListAsync(cancellationToken);

        return changes.Select(change => new ProfileChangeResponse(
            change.Id,
            change.CompanyId,
            versions.GetValueOrDefault(change.OldProfileVersionId),
            versions.GetValueOrDefault(change.NewProfileVersionId),
            change.FieldPath,
            change.ItemKey,
            change.ChangeType,
            change.OldValueJson,
            change.NewValueJson,
            change.DetectedAt)).ToArray();
    }
}
