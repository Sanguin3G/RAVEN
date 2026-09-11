using Microsoft.AspNetCore.Http.HttpResults;
using Raven.Api.Features.Research;

namespace Raven.Api.Features.Profiles.Enrichment;

public static class TargetedProfileUpdateEndpoints
{
    public static IEndpointRouteBuilder MapTargetedProfileUpdateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/companies/{companyId:guid}/research/targeted", StartAsync)
            .WithTags("Research", "Profiles")
            .WithSummary("Start target-scoped evidence research from an accepted Company Profile")
            .Produces<ResearchRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/research-runs/{researchRunId:guid}/profile-patch/generate", GenerateAsync)
            .WithTags("Profiles")
            .WithSummary("Generate a reviewable patch that preserves unrelated accepted profile fields")
            .Produces<ProfilePatchCandidate>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/research-runs/{researchRunId:guid}/profile-patch/confirm", ConfirmAsync)
            .WithTags("Profiles")
            .WithSummary("Confirm a server-owned targeted profile patch as a normal immutable profile version")
            .Produces<CompanyProfileVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<Results<Ok<ResearchRunResponse>, NotFound>> StartAsync(
        Guid companyId, StartTargetedResearchRequest request, ITargetedProfileUpdateService service, CancellationToken cancellationToken)
    {
        var run = await service.StartAsync(companyId, request, cancellationToken);
        return run is null ? TypedResults.NotFound() : TypedResults.Ok(run);
    }

    private static async Task<Results<Ok<ProfilePatchCandidate>, NotFound>> GenerateAsync(
        Guid researchRunId, ITargetedProfileUpdateService service, CancellationToken cancellationToken)
    {
        var patch = await service.GenerateAsync(researchRunId, cancellationToken);
        return patch is null ? TypedResults.NotFound() : TypedResults.Ok(patch);
    }

    private static async Task<Results<Ok<CompanyProfileVersion>, NotFound>> ConfirmAsync(
        Guid researchRunId, ConfirmCompanyProfileRequest request, ITargetedProfileUpdateService service, CancellationToken cancellationToken)
    {
        var profile = await service.ConfirmAsync(researchRunId, request.CandidateId, cancellationToken);
        return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
    }
}
