using Raven.Api.Features.Monitoring;

namespace Raven.Api.Tests;

public sealed class CompanyMonitoringServiceTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(MonitoringCadence.Daily, 1, 9, 12)]
    [InlineData(MonitoringCadence.Weekly, 7, 9, 18)]
    [InlineData(MonitoringCadence.Monthly, 30, 10, 11)]
    public void CalculateNextRun_is_deterministic_for_supported_cadences(
        MonitoringCadence cadence,
        int expectedDays,
        int expectedMonth,
        int expectedDay)
    {
        var service = new CompanyMonitoringService(new TestClock(Now));

        var next = service.CalculateNextRun(cadence, Now);

        Assert.Equal(Now.AddDays(expectedDays).Date, next.Date);
        Assert.Equal(new DateTimeOffset(2026, expectedMonth, expectedDay, 8, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Enable_schedules_the_first_run_after_one_cadence()
    {
        var service = new CompanyMonitoringService(new TestClock(Now));

        var setting = service.Enable(CompanyId, MonitoringCadence.Weekly);

        Assert.Equal(CompanyId, setting.CompanyId);
        Assert.True(setting.Enabled);
        Assert.Equal(MonitoringCadence.Weekly, setting.Cadence);
        Assert.Equal(Now.AddDays(7), setting.NextRunAt);
        Assert.Equal(Now, setting.CreatedAt);
        Assert.Equal(Now, setting.UpdatedAt);
        Assert.Null(setting.LastRunStatus);
    }

    [Fact]
    public void A_setting_is_due_only_when_enabled_and_not_actively_claimed()
    {
        var clock = new TestClock(Now.AddDays(7));
        var service = new CompanyMonitoringService(clock);
        var setting = service.Enable(CompanyId, MonitoringCadence.Weekly, Now.AddDays(-7));
        var claimId = Guid.NewGuid();

        Assert.True(service.IsDue(setting));
        Assert.True(service.TryClaim(setting, claimId, TimeSpan.FromMinutes(10)));
        Assert.False(service.IsDue(setting));

        clock.UtcNowValue = Now.AddDays(7).AddMinutes(11);
        Assert.True(service.IsDue(setting));
    }

    [Fact]
    public void An_active_claim_cannot_be_claimed_twice_before_lease_expiry()
    {
        var service = new CompanyMonitoringService(new TestClock(Now.AddDays(7)));
        var setting = service.Enable(CompanyId, MonitoringCadence.Weekly, Now.AddDays(-7));

        Assert.True(service.TryClaim(setting, Guid.NewGuid(), TimeSpan.FromMinutes(30)));
        Assert.False(service.TryClaim(setting, Guid.NewGuid(), TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void Completion_marks_update_ready_for_review_and_never_confirms_profile()
    {
        var completionTime = Now.AddDays(7).AddMinutes(5);
        var service = new CompanyMonitoringService(new TestClock(completionTime));
        var setting = service.Enable(CompanyId, MonitoringCadence.Weekly, Now.AddDays(-7));
        var claimId = Guid.NewGuid();
        Assert.True(service.TryClaim(setting, claimId, TimeSpan.FromMinutes(30), Now.AddDays(7)));

        service.MarkUpdateReadyForReview(setting, claimId, completionTime);

        Assert.Equal(MonitoringRunStatus.ReadyForReview, setting.LastRunStatus);
        Assert.Equal(completionTime, setting.LastRunAt);
        Assert.Equal(completionTime.AddDays(7), setting.NextRunAt);
        Assert.Null(setting.ActiveClaimId);
        Assert.Null(setting.ClaimExpiresAt);
    }

    [Fact]
    public void A_worker_cannot_complete_a_run_with_another_worker_claim()
    {
        var service = new CompanyMonitoringService(new TestClock(Now.AddDays(7)));
        var setting = service.Enable(CompanyId, MonitoringCadence.Weekly, Now.AddDays(-7));
        Assert.True(service.TryClaim(setting, Guid.NewGuid(), TimeSpan.FromMinutes(30)));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.MarkUpdateReadyForReview(setting, Guid.NewGuid()));

        Assert.Contains("claim", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestClock(DateTimeOffset initialValue) : IMonitoringClock
    {
        public DateTimeOffset UtcNowValue { get; set; } = initialValue;
        public DateTimeOffset UtcNow => UtcNowValue;
    }
}
