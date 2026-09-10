using Microsoft.AspNetCore.Http.HttpResults;

namespace Raven.Api.Features.Profiles;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/research-runs/{researchRunId:guid}/profile/generate", GenerateAsync)
            .WithTags("Profiles")
            .WithSummary("Generate an evidence-grounded profile candidate")
            .Produces<ProfileGenerationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/research-runs/{researchRunId:guid}/profile/confirm", ConfirmAsync)
            .WithTags("Profiles")
            .WithSummary("Confirm a server-owned Company Profile candidate")
            .Produces<CompanyProfileVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/companies/{companyId:guid}/profile", GetCurrentAsync)
            .WithTags("Profiles")
            .WithSummary("Get the latest accepted Company Profile")
            .Produces<CompanyProfileVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<ProfileGenerationResponse>, NotFound>> GenerateAsync(
        Guid researchRunId, ICompanyProfileWorkflowService profiles, CancellationToken cancellationToken)
    {
        var result = await profiles.GenerateAsync(researchRunId, cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<CompanyProfileVersion>, NotFound>> ConfirmAsync(
        Guid researchRunId, ConfirmCompanyProfileRequest request, ICompanyProfileWorkflowService profiles, CancellationToken cancellationToken)
    {
        var profile = await profiles.ConfirmAsync(researchRunId, request.CandidateId, cancellationToken);
        return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
    }

    private static async Task<Results<Ok<CompanyProfileVersion>, NotFound>> GetCurrentAsync(
        Guid companyId, ICompanyProfileWorkflowService profiles, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetCurrentAsync(companyId, cancellationToken);
        return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
    }
}
