namespace Raven.Api.Features.Monitoring;

public sealed class CompanyMonitoringService : ICompanyMonitoringService
{
    private readonly IMonitoringClock clock;

    public CompanyMonitoringService(IMonitoringClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public CompanyMonitoringSetting Enable(
        Guid companyId,
        MonitoringCadence cadence,
        DateTimeOffset? enabledAt = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A monitoring setting requires a company ID.", nameof(companyId));
        }

        var now = enabledAt ?? clock.UtcNow;
        var setting = new CompanyMonitoringSetting
        {
            CompanyId = companyId,
            Enabled = true,
            Cadence = cadence,
            NextRunAt = CalculateNextRun(cadence, now),
            CreatedAt = now,
            UpdatedAt = now
        };

        return setting;
    }

    public void Disable(CompanyMonitoringSetting setting, DateTimeOffset? disabledAt = null)
    {
        ArgumentNullException.ThrowIfNull(setting);

        var now = disabledAt ?? clock.UtcNow;
        lock (setting)
        {
            setting.Enabled = false;
            setting.ActiveClaimId = null;
            setting.ClaimExpiresAt = null;
            setting.UpdatedAt = now;
        }
    }

    public DateTimeOffset CalculateNextRun(MonitoringCadence cadence, DateTimeOffset from)
    {
        return cadence switch
        {
            MonitoringCadence.Daily => from.AddDays(1),
            MonitoringCadence.Weekly => from.AddDays(7),
            MonitoringCadence.Monthly => from.AddMonths(1),
            _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unsupported monitoring cadence.")
        };
    }

    public bool IsDue(CompanyMonitoringSetting setting, DateTimeOffset? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(setting);

        var now = asOf ?? clock.UtcNow;
        lock (setting)
        {
            return setting.Enabled &&
                   setting.NextRunAt <= now &&
                   (setting.ClaimExpiresAt is null || setting.ClaimExpiresAt <= now);
        }
    }

    public bool TryClaim(
        CompanyMonitoringSetting setting,
        Guid claimId,
        TimeSpan leaseDuration,
        DateTimeOffset? claimedAt = null)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (claimId == Guid.Empty)
        {
            throw new ArgumentException("A scheduled run claim requires a claim ID.", nameof(claimId));
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "A claim lease must be positive.");
        }

        var now = claimedAt ?? clock.UtcNow;
        lock (setting)
        {
            if (!setting.Enabled ||
                setting.NextRunAt > now ||
                (setting.ClaimExpiresAt is not null && setting.ClaimExpiresAt > now))
            {
                return false;
            }

            setting.ActiveClaimId = claimId;
            setting.ClaimExpiresAt = now.Add(leaseDuration);
            setting.LastRunStatus = MonitoringRunStatus.Running;
            setting.UpdatedAt = now;
            return true;
        }
    }

    public void MarkUpdateReadyForReview(
        CompanyMonitoringSetting setting,
        Guid claimId,
        DateTimeOffset? completedAt = null)
    {
        CompleteScheduledRun(setting, claimId, MonitoringRunStatus.ReadyForReview, completedAt);
    }

    public void MarkFailed(
        CompanyMonitoringSetting setting,
        Guid claimId,
        DateTimeOffset? failedAt = null)
    {
        CompleteScheduledRun(setting, claimId, MonitoringRunStatus.Failed, failedAt);
    }

    public void MarkCancelled(
        CompanyMonitoringSetting setting,
        Guid claimId,
        DateTimeOffset? cancelledAt = null)
    {
        CompleteScheduledRun(setting, claimId, MonitoringRunStatus.Cancelled, cancelledAt);
    }

    private void CompleteScheduledRun(
        CompanyMonitoringSetting setting,
        Guid claimId,
        MonitoringRunStatus status,
        DateTimeOffset? completedAt)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (claimId == Guid.Empty)
        {
            throw new ArgumentException("A scheduled run claim requires a claim ID.", nameof(claimId));
        }

        var now = completedAt ?? clock.UtcNow;
        lock (setting)
        {
            if (setting.ActiveClaimId != claimId)
            {
                throw new InvalidOperationException("The scheduled run claim is missing or belongs to another worker.");
            }

            setting.LastRunAt = now;
            setting.LastRunStatus = status;
            // This advances the schedule even when a run fails. A later worker can
            // retry at the next cadence, while a review-ready result remains pending
            // for the user and never auto-confirms a profile.
            setting.NextRunAt = CalculateNextRun(setting.Cadence, now);
            setting.ActiveClaimId = null;
            setting.ClaimExpiresAt = null;
            setting.UpdatedAt = now;
        }
    }
}
