using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Monitoring;

/// <summary>EF store with a lease claim suitable for RAVEN's one in-process worker.</summary>
public sealed class EfCompanyMonitoringStore(RavenDbContext dbContext) : ICompanyMonitoringStore
{
    public async Task<CompanyMonitoringSetting?> GetAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await dbContext.CompanyMonitoringSettings.AsNoTracking()
            .SingleOrDefaultAsync(setting => setting.CompanyId == companyId, cancellationToken);

    public async Task<IReadOnlyList<CompanyMonitoringSetting>> ListDueAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default) =>
        // SQLite cannot translate DateTimeOffset relational comparisons. The
        // monitoring list is deliberately tiny (at most 100 enabled rows) in
        // this single-instance MVP, so evaluate the lease/due comparison after
        // the safe SQL filter instead of storing fragile string timestamps.
        (await dbContext.CompanyMonitoringSettings.AsNoTracking()
            .Where(setting => setting.Enabled)
            .ToListAsync(cancellationToken))
            .Where(setting => setting.NextRunAt <= asOf &&
                              (setting.ClaimExpiresAt is null || setting.ClaimExpiresAt <= asOf))
            .OrderBy(setting => setting.NextRunAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArray();

    public async Task<CompanyMonitoringSetting?> TryClaimDueAsync(
        Guid companyId,
        Guid claimId,
        DateTimeOffset claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var setting = await dbContext.CompanyMonitoringSettings
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Enabled, cancellationToken);
        if (setting is null || setting.NextRunAt > claimedAt ||
            (setting.ClaimExpiresAt is not null && setting.ClaimExpiresAt > claimedAt))
        {
            return null;
        }

        setting.ActiveClaimId = claimId;
        setting.ClaimExpiresAt = claimedAt.Add(leaseDuration);
        setting.LastRunStatus = MonitoringRunStatus.Running;
        setting.UpdatedAt = claimedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return setting;
    }

    public async Task SaveAsync(CompanyMonitoringSetting setting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var existing = await dbContext.CompanyMonitoringSettings
            .SingleOrDefaultAsync(item => item.CompanyId == setting.CompanyId, cancellationToken);
        if (existing is null)
        {
            dbContext.CompanyMonitoringSettings.Add(setting);
        }
        else
        {
            existing.Enabled = setting.Enabled;
            existing.Cadence = setting.Cadence;
            existing.NextRunAt = setting.NextRunAt;
            existing.LastRunAt = setting.LastRunAt;
            existing.LastRunStatus = setting.LastRunStatus;
            existing.ActiveClaimId = setting.ActiveClaimId;
            existing.ClaimExpiresAt = setting.ClaimExpiresAt;
            existing.UpdatedAt = setting.UpdatedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
