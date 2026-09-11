namespace Raven.Api.Features.Monitoring;

/// <summary>
/// Per-company scheduled research preference and the scheduler's small amount of
/// durable state. CompanyId is the natural key: a company has at most one active
/// monitoring preference in the MVP.
/// </summary>
public sealed class CompanyMonitoringSetting
{
    public Guid CompanyId { get; init; }
    public bool Enabled { get; set; }
    public MonitoringCadence Cadence { get; set; } = MonitoringCadence.Weekly;
    public DateTimeOffset NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public MonitoringRunStatus? LastRunStatus { get; set; }

    /// <summary>
    /// Durable lease fields used by an EF implementation's atomic claim operation.
    /// An expired lease can be reclaimed after an interrupted worker execution.
    /// </summary>
    public Guid? ActiveClaimId { get; set; }
    public DateTimeOffset? ClaimExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
