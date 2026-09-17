using Microsoft.AspNetCore.Http.HttpResults;
using Raven.Api.Features.Companies;

namespace Raven.Api.Features.Research.SavedArtifacts;

public static class SavedResearchEndpoints
{
    public static IEndpointRouteBuilder MapSavedResearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/companies/{companyId:guid}/saved-research", ListSavedAsync)
            .WithTags("Saved Research")
            .WithName("ListCompanySavedResearch")
            .WithSummary("List saved research artifacts for a company")
            .Produces<SavedResearchArtifactResponse[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/companies/{companyId:guid}/saved-research/{artifactId:guid}", GetSavedAsync)
            .WithTags("Saved Research")
            .WithName("GetCompanySavedResearch")
            .WithSummary("Get one saved research artifact scoped to a company")
            .Produces<SavedResearchArtifactResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<SavedResearchArtifactResponse[]>, NotFound>> ListSavedAsync(
        Guid companyId,
        ICompanyService companies,
        ISavedResearchArtifactService artifacts,
        CancellationToken cancellationToken)
    {
        if (await companies.GetByIdAsync(companyId, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        var response = (await artifacts.ListAsync(companyId, cancellationToken))
            .Select(SavedResearchArtifactResponse.FromEntity)
            .ToArray();
        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<SavedResearchArtifactResponse>, NotFound>> GetSavedAsync(
        Guid companyId,
        Guid artifactId,
        ISavedResearchArtifactService artifacts,
        CancellationToken cancellationToken)
    {
        var artifact = await artifacts.GetAsync(companyId, artifactId, cancellationToken);
        return artifact is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(SavedResearchArtifactResponse.FromEntity(artifact));
    }
}