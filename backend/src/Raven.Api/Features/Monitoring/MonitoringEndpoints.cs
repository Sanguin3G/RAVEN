using Microsoft.AspNetCore.Http.HttpResults;

namespace Raven.Api.Features.Monitoring;

public static class MonitoringEndpoints
{
    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/companies/{companyId:guid}/monitoring", GetAsync)
            .WithTags("Monitoring")
            .WithSummary("Get Company Monitoring settings")
            .Produces<CompanyMonitoringResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPut("/api/companies/{companyId:guid}/monitoring", UpdateAsync)
            .WithTags("Monitoring")
            .WithSummary("Enable, disable, or set Company Monitoring cadence")
            .Produces<CompanyMonitoringResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<CompanyMonitoringResponse>, NotFound>> GetAsync(
        Guid companyId,
        ICompanyMonitoringCoordinator monitoring,
        CancellationToken cancellationToken)
    {
        var setting = await monitoring.GetAsync(companyId, cancellationToken);
        return setting is null ? TypedResults.NotFound() : TypedResults.Ok(setting);
    }

    private static async Task<Results<Ok<CompanyMonitoringResponse>, NotFound>> UpdateAsync(
        Guid companyId,
        UpdateCompanyMonitoringRequest request,
        ICompanyMonitoringCoordinator monitoring,
        CancellationToken cancellationToken)
    {
        var setting = await monitoring.UpdateAsync(companyId, request, cancellationToken);
        return setting is null ? TypedResults.NotFound() : TypedResults.Ok(setting);
    }
}
