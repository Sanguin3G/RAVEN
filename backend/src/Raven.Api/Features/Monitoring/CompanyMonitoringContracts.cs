namespace Raven.Api.Features.Monitoring;

/// <summary>
/// The cadence at which an enabled company is considered for scheduled research.
/// </summary>
public enum MonitoringCadence
{
    Daily,
    Weekly,
    Monthly
}

/// <summary>
/// The last scheduled-run outcome recorded on a monitoring setting.
/// A ready-for-review outcome is deliberately distinct from a completed/confirmed
/// profile update: scheduled monitoring never silently changes accepted profile data.
/// </summary>
public enum MonitoringRunStatus
{
    Running,
    ReadyForReview,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Time source used by monitoring domain logic. Keeping this behind an interface
/// makes due-date and scheduling tests deterministic and keeps the worker free from
/// direct calls to the system clock.
/// </summary>
public interface IMonitoringClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemMonitoringClock : IMonitoringClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Repository seam for the future EF-backed monitoring store.
/// TryClaimDueAsync must be implemented as one atomic conditional update (or an
/// equivalent transaction) so two worker polls cannot claim the same company.
/// </summary>
public interface ICompanyMonitoringStore
{
    Task<CompanyMonitoringSetting?> GetAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyMonitoringSetting>> ListDueAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CompanyMonitoringSetting?> TryClaimDueAsync(
        Guid companyId,
        Guid claimId,
        DateTimeOffset claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        CompanyMonitoringSetting setting,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Application-facing monitoring domain service. It owns cadence arithmetic,
/// claim validation and the review-required outcome policy; persistence remains in
/// the store implementation owned by the integration layer.
/// </summary>
public interface ICompanyMonitoringService
{
    CompanyMonitoringSetting Enable(
        Guid companyId,
        MonitoringCadence cadence,
        DateTimeOffset? enabledAt = null);

    void Disable(CompanyMonitoringSetting setting, DateTimeOffset? disabledAt = null);

    DateTimeOffset CalculateNextRun(
        MonitoringCadence cadence,
        DateTimeOffset from);

    bool IsDue(CompanyMonitoringSetting setting, DateTimeOffset? asOf = null);

    bool TryClaim(
        CompanyMonitoringSetting setting,
        Guid claimId,
        TimeSpan leaseDuration,
        DateTimeOffset? claimedAt = null);

    void MarkUpdateReadyForReview(
        CompanyMonitoringSetting setting,
        Guid claimId,
        DateTimeOffset? completedAt = null);

    void MarkFailed(
        CompanyMonitoringSetting setting,
        Guid claimId,
        DateTimeOffset? failedAt = null);

    void MarkCancelled(
        CompanyMonitoringSetting setting,
        Guid claimId,
        DateTimeOffset? cancelledAt = null);
}
