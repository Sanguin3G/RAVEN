using Microsoft.AspNetCore.Http.HttpResults;

namespace Raven.Api.Features.Research.Organization;

public static class InvestigationOrganizationEndpoints
{
    public static IEndpointRouteBuilder MapInvestigationOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/companies/{companyId:guid}/saved-research/{artifactId:guid}/organization", GetAsync)
            .WithTags("Investigations")
            .WithName("GetInvestigationOrganization")
            .WithSummary("Get the current derived organization of saved research")
            .Produces<InvestigationOrganizationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/companies/{companyId:guid}/saved-research/{artifactId:guid}/organization", OrganizeAsync)
            .WithTags("Investigations")
            .WithName("OrganizeInvestigation")
            .WithSummary("Create a new versioned organization of saved research")
            .WithDescription("Organization is derived from the saved artifact. Raw research material and earlier organization revisions are preserved.")
            .Produces<InvestigationOrganizationResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<InvestigationOrganizationResponse>, NotFound>> GetAsync(
        Guid companyId,
        Guid artifactId,
        IInvestigationOrganizationService service,
        CancellationToken cancellationToken)
    {
        var response = await service.GetCurrentAsync(companyId, artifactId, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

    private static async Task<Results<Created<InvestigationOrganizationResponse>, NotFound>> OrganizeAsync(
        Guid companyId,
        Guid artifactId,
        IInvestigationOrganizationService service,
        CancellationToken cancellationToken)
    {
        var response = await service.OrganizeAsync(companyId, artifactId, cancellationToken);
        return response is null
            ? TypedResults.NotFound()
            : TypedResults.Created($"/api/companies/{companyId:D}/saved-research/{artifactId:D}/organization", response);
    }
}
