using Microsoft.AspNetCore.Http.HttpResults;

namespace Raven.Api.Features.Research.SavedArtifacts;

public static class InvestigationEndpoints
{
    public static IEndpointRouteBuilder MapInvestigationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/investigations").WithTags("Investigations");
        group.MapGet("/", ListAsync).WithName("ListInvestigations").WithSummary("List company investigations and their review state")
            .Produces<InvestigationResponse[]>(StatusCodes.Status200OK);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetInvestigation").WithSummary("Get one investigation")
            .Produces<InvestigationResponse>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/done", DoneAsync).WithName("MarkInvestigationDone").WithSummary("Mark research material handled through its current update")
            .Produces<InvestigationResponse>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);
        group.MapPost("/{id:guid}/reopen", ReopenAsync).WithName("ReopenInvestigation").WithSummary("Reopen a handled investigation")
            .Produces<InvestigationResponse>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<Ok<IReadOnlyList<InvestigationResponse>>> ListAsync(Guid companyId, InvestigationService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListAsync(companyId, ct));

    private static async Task<Results<Ok<InvestigationResponse>, NotFound>> GetAsync(Guid companyId, Guid id, InvestigationService service, CancellationToken ct)
    {
        var item = await service.GetAsync(companyId, id, ct);
        return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
    }

    private static async Task<Results<Ok<InvestigationResponse>, NotFound, Conflict<string>>> DoneAsync(Guid companyId, Guid id, InvestigationService service, CancellationToken ct)
    {
        try
        {
            var item = await service.MarkDoneAsync(companyId, id, ct);
            return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
        }
        catch (InvalidOperationException exception) { return TypedResults.Conflict(exception.Message); }
    }

    private static async Task<Results<Ok<InvestigationResponse>, NotFound, Conflict<string>>> ReopenAsync(Guid companyId, Guid id, InvestigationService service, CancellationToken ct)
    {
        try
        {
            var item = await service.ReopenAsync(companyId, id, ct);
            return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
        }
        catch (InvalidOperationException exception) { return TypedResults.Conflict(exception.Message); }
    }
}
