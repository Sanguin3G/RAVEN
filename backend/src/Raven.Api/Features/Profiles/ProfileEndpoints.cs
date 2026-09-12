using Microsoft.AspNetCore.Http.HttpResults;
using Raven.Api.Features.Profiles.Changes;

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

        app.MapGet("/api/research-runs/{researchRunId:guid}/profile/candidate", GetCandidateAsync)
            .WithTags("Profiles")
            .WithSummary("Get the latest server-owned Company Profile candidate for a research run")
            .Produces<CompanyProfileCandidate>(StatusCodes.Status200OK)
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

        app.MapGet("/api/companies/{companyId:guid}/profile/versions", ListVersionsAsync)
            .WithTags("Profiles")
            .WithSummary("List immutable Company Profile versions, newest first")
            .Produces<CompanyProfileVersion[]>(StatusCodes.Status200OK);

        app.MapGet("/api/companies/{companyId:guid}/profile/versions/{version:int}", GetVersionAsync)
            .WithTags("Profiles")
            .WithSummary("Get one immutable Company Profile version")
            .Produces<CompanyProfileVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/companies/{companyId:guid}/profile/changes", ListLatestChangesAsync)
            .WithTags("Profiles")
            .WithSummary("List persisted changes from the latest accepted profile update")
            .Produces<ProfileChangeResponse[]>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<Results<Ok<ProfileGenerationResponse>, NotFound>> GenerateAsync(
        Guid researchRunId, ICompanyProfileWorkflowService profiles, CancellationToken cancellationToken)
    {
        var result = await profiles.GenerateAsync(researchRunId, cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<CompanyProfileCandidate>, NotFound>> GetCandidateAsync(
        Guid researchRunId, ICompanyProfileWorkflowService profiles, CancellationToken cancellationToken)
    {
        var candidate = await profiles.GetCandidateAsync(researchRunId, cancellationToken);
        return candidate is null ? TypedResults.NotFound() : TypedResults.Ok(candidate);
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

    private static async Task<Ok<CompanyProfileVersion[]>> ListVersionsAsync(
        Guid companyId, ICompanyProfileWorkflowService profiles, CancellationToken cancellationToken) =>
        TypedResults.Ok((await profiles.ListVersionsAsync(companyId, cancellationToken)).ToArray());

    private static async Task<Results<Ok<CompanyProfileVersion>, NotFound>> GetVersionAsync(
        Guid companyId, int version, ICompanyProfileWorkflowService profiles, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetVersionAsync(companyId, version, cancellationToken);
        return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
    }

    private static async Task<Ok<ProfileChangeResponse[]>> ListLatestChangesAsync(
        Guid companyId, IProfileChangeQueryService changes, CancellationToken cancellationToken) =>
        TypedResults.Ok((await changes.ListLatestAsync(companyId, cancellationToken)).ToArray());
}
