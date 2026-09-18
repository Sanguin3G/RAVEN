using Microsoft.AspNetCore.Http.HttpResults;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>HTTP transport for asynchronous, provider-managed research jobs.</summary>
public static class ManagedResearchEndpoints
{
    public static IEndpointRouteBuilder MapManagedResearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/companies/{companyId:guid}/managed-research", StartAsync)
            .WithTags("Managed Research")
            .WithName("StartManagedResearch")
            .WithSummary("Queue managed AI research for a company")
            .WithDescription("Starts an asynchronous provider run. Results remain investigation material and never update the accepted Company Profile automatically.")
            .Produces<ManagedResearchJobResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/companies/{companyId:guid}/managed-research/{jobId:guid}", GetAsync)
            .WithTags("Managed Research")
            .WithName("GetManagedResearch")
            .WithSummary("Poll a managed AI research job")
            .Produces<ManagedResearchJobResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/companies/{companyId:guid}/managed-research", ListAsync)
            .WithTags("Managed Research")
            .WithName("ListManagedResearch")
            .WithSummary("List durable managed AI research jobs for a company")
            .Produces<ManagedResearchJobResponse[]>(StatusCodes.Status200OK);

        app.MapPost("/api/companies/{companyId:guid}/managed-research/{jobId:guid}/cancel", CancelAsync)
            .WithTags("Managed Research")
            .WithName("CancelManagedResearch")
            .WithSummary("Request cancellation of managed AI research")
            .Produces<ManagedResearchJobResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Accepted<ManagedResearchJobResponse>, BadRequest, NotFound>> StartAsync(
        Guid companyId,
        StartManagedResearchRequest request,
        IManagedResearchJobService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await service.StartAsync(companyId, request, cancellationToken);
            return TypedResults.Accepted(
                $"/api/companies/{companyId:D}/managed-research/{response.Id:D}",
                response);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest();
        }
    }

    private static async Task<Results<Ok<ManagedResearchJobResponse>, NotFound>> GetAsync(
        Guid companyId,
        Guid jobId,
        IManagedResearchJobService service,
        CancellationToken cancellationToken)
    {
        var response = await service.GetAsync(companyId, jobId, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

    private static async Task<Ok<ManagedResearchJobResponse[]>> ListAsync(
        Guid companyId,
        IManagedResearchJobService service,
        CancellationToken cancellationToken) =>
        TypedResults.Ok((await service.ListForCompanyAsync(companyId, cancellationToken)).ToArray());

    private static async Task<Results<Ok<ManagedResearchJobResponse>, NotFound>> CancelAsync(
        Guid companyId,
        Guid jobId,
        IManagedResearchJobService service,
        CancellationToken cancellationToken)
    {
        var response = await service.CancelAsync(companyId, jobId, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

}
