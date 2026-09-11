using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Monitoring;

/// <summary>EF store with a conditional database claim for single-instance-safe polling.</summary>
public sealed class EfCompanyMonitoringStore(RavenDbContext dbContext) : ICompanyMonitoringStore
{
    public async Task<CompanyMonitoringSetting?> GetAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await dbContext.CompanyMonitoringSettings.AsNoTracking()
            .SingleOrDefaultAsync(setting => setting.CompanyId == companyId, cancellationToken);

    public async Task<IReadOnlyList<CompanyMonitoringSetting>> ListDueAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default) =>
        await dbContext.CompanyMonitoringSettings.AsNoTracking()
            .Where(setting => setting.Enabled && setting.NextRunAt <= asOf &&
                              (setting.ClaimExpiresAt == null || setting.ClaimExpiresAt <= asOf))
            .OrderBy(setting => setting.NextRunAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);

    public async Task<CompanyMonitoringSetting?> TryClaimDueAsync(
        Guid companyId,
        Guid claimId,
        DateTimeOffset claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var expiresAt = claimedAt.Add(leaseDuration);
        var claimed = await dbContext.CompanyMonitoringSettings
            .Where(setting => setting.CompanyId == companyId && setting.Enabled && setting.NextRunAt <= claimedAt &&
                              (setting.ClaimExpiresAt == null || setting.ClaimExpiresAt <= claimedAt))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(setting => setting.ActiveClaimId, claimId)
                .SetProperty(setting => setting.ClaimExpiresAt, expiresAt)
                .SetProperty(setting => setting.LastRunStatus, MonitoringRunStatus.Running)
                .SetProperty(setting => setting.UpdatedAt, claimedAt), cancellationToken);

        return claimed == 0
            ? null
            : await GetAsync(companyId, cancellationToken);
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
