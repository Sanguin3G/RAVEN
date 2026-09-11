using Raven.Api.Features.Companies;

namespace Raven.Api.Features.Monitoring;

public sealed record CompanyMonitoringResponse(
    Guid CompanyId,
    bool Enabled,
    MonitoringCadence Cadence,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    MonitoringRunStatus? LastRunStatus);

public sealed record UpdateCompanyMonitoringRequest(bool Enabled, MonitoringCadence Cadence = MonitoringCadence.Weekly);

public interface ICompanyMonitoringCoordinator
{
    Task<CompanyMonitoringResponse?> GetAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<CompanyMonitoringResponse?> UpdateAsync(Guid companyId, UpdateCompanyMonitoringRequest request, CancellationToken cancellationToken = default);
}

public sealed class CompanyMonitoringCoordinator(
    ICompanyMonitoringStore store,
    ICompanyMonitoringService monitoring,
    ICompanyService companies,
    IMonitoringClock clock) : ICompanyMonitoringCoordinator
{
    public async Task<CompanyMonitoringResponse?> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (await companies.GetByIdAsync(companyId, cancellationToken) is null)
        {
            return null;
        }

        return ToResponse(await store.GetAsync(companyId, cancellationToken), companyId);
    }

    public async Task<CompanyMonitoringResponse?> UpdateAsync(
        Guid companyId,
        UpdateCompanyMonitoringRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await companies.GetByIdAsync(companyId, cancellationToken) is null)
        {
            return null;
        }

        var existing = await store.GetAsync(companyId, cancellationToken);
        if (request.Enabled)
        {
            var enabled = monitoring.Enable(companyId, request.Cadence, clock.UtcNow);
            if (existing is not null)
            {
                enabled.CreatedAt = existing.CreatedAt;
                enabled.LastRunAt = existing.LastRunAt;
                enabled.LastRunStatus = existing.LastRunStatus;
            }
            await store.SaveAsync(enabled, cancellationToken);
            return ToResponse(enabled, companyId);
        }

        var disabled = existing ?? new CompanyMonitoringSetting
        {
            CompanyId = companyId,
            Enabled = false,
            Cadence = request.Cadence,
            NextRunAt = clock.UtcNow,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        monitoring.Disable(disabled, clock.UtcNow);
        await store.SaveAsync(disabled, cancellationToken);
        return ToResponse(disabled, companyId);
    }

    private static CompanyMonitoringResponse ToResponse(CompanyMonitoringSetting? setting, Guid companyId) =>
        setting is null
            ? new CompanyMonitoringResponse(companyId, false, MonitoringCadence.Weekly, null, null, null)
            : new CompanyMonitoringResponse(
                setting.CompanyId,
                setting.Enabled,
                setting.Cadence,
                setting.Enabled ? setting.NextRunAt : null,
                setting.LastRunAt,
                setting.LastRunStatus);
}
