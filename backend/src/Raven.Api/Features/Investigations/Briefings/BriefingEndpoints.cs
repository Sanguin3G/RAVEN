using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Raven.Api.Features.Research.Briefings;

public static class BriefingEndpoints
{
    public static IEndpointRouteBuilder MapBriefingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/briefings").WithTags("Briefings");
        group.MapGet("/", ListAsync).WithName("ListBriefings").WithSummary("List current company Briefings")
            .Produces<BriefingListItemResponse[]>(StatusCodes.Status200OK);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetBriefing").WithSummary("Get the current Briefing version")
            .Produces<BriefingResponse>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        group.MapPost("/", CreateAsync).WithName("CreateBriefing").WithSummary("Queue Briefing generation from selected persisted Investigations")
            .Produces<BriefingGenerationJobResponse>(StatusCodes.Status202Accepted).ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/versions", UpdateAsync).WithName("UpdateBriefing").WithSummary("Queue an immutable Briefing version from selected new Investigations")
            .Produces<BriefingGenerationJobResponse>(StatusCodes.Status202Accepted).Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/generation-jobs/{jobId:guid}", GenerationJobAsync).WithName("GetBriefingGenerationJob")
            .WithSummary("Get Briefing generation status").Produces<BriefingGenerationJobResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/versions", VersionsAsync).WithName("ListBriefingVersions").WithSummary("List immutable Briefing versions")
            .Produces<BriefingVersionResponse[]>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/versions/{number:int}", VersionAsync).WithName("GetBriefingVersion").WithSummary("Get an earlier Briefing version")
            .Produces<BriefingVersionResponse>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/versions/{number:int}/changes", CompareAsync).WithName("CompareBriefingVersion").WithSummary("Compare selected material between adjacent Briefing versions")
            .Produces<BriefingChangeResponse>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/newer-investigations", NewerAsync).WithName("GetNewerBriefingInvestigations").WithSummary("Suggest newer relevant persisted Investigations")
            .Produces<InvestigationCandidateResponse[]>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<Ok<IReadOnlyList<BriefingListItemResponse>>> ListAsync(Guid companyId, BriefingService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListAsync(companyId, ct));

    private static async Task<Results<Ok<BriefingResponse>, NotFound>> GetAsync(Guid companyId, Guid id, BriefingService service, CancellationToken ct)
    {
        var brief = await service.GetAsync(companyId, id, ct);
        return brief is null ? TypedResults.NotFound() : TypedResults.Ok(brief);
    }

    private static async Task<IResult> CreateAsync(Guid companyId, CreateBriefingRequest request, BriefingGenerationService service, CancellationToken ct)
    {
        try
        {
            var job = await service.StartCreateAsync(companyId, request, ct);
            return TypedResults.Accepted($"/api/companies/{companyId:D}/briefings/generation-jobs/{job.Id:D}", job);
        }
        catch (ArgumentException exception) { return TypedResults.Problem(exception.Message, statusCode: 400); }
        catch (KeyNotFoundException) { return TypedResults.NotFound(); }
    }

    private static async Task<IResult> UpdateAsync(Guid companyId, Guid id, UpdateBriefingRequest request, BriefingGenerationService service, CancellationToken ct)
    {
        try
        {
            var job = await service.StartUpdateAsync(companyId, id, request, ct);
            return job is null ? TypedResults.NotFound() : TypedResults.Accepted($"/api/companies/{companyId:D}/briefings/generation-jobs/{job.Id:D}", job);
        }
        catch (ArgumentException exception) { return TypedResults.Problem(exception.Message, statusCode: 400); }
        catch (InvalidOperationException exception) { return TypedResults.Problem(exception.Message, statusCode: 400); }
    }

    private static async Task<Results<Ok<BriefingGenerationJobResponse>, NotFound>> GenerationJobAsync(
        Guid companyId, Guid jobId, BriefingGenerationService service, CancellationToken ct)
    {
        var job = await service.GetAsync(companyId, jobId, ct);
        return job is null ? TypedResults.NotFound() : TypedResults.Ok(job);
    }

    private static async Task<Results<Ok<IReadOnlyList<BriefingVersionResponse>>, NotFound>> VersionsAsync(Guid companyId, Guid id, BriefingService service, CancellationToken ct)
    {
        var versions = await service.VersionsAsync(companyId, id, ct);
        return versions is null ? TypedResults.NotFound() : TypedResults.Ok(versions);
    }

    private static async Task<Results<Ok<BriefingVersionResponse>, NotFound>> VersionAsync(Guid companyId, Guid id, int number, BriefingService service, CancellationToken ct)
    {
        var version = await service.VersionAsync(companyId, id, number, ct);
        return version is null ? TypedResults.NotFound() : TypedResults.Ok(version);
    }

    private static async Task<Results<Ok<BriefingChangeResponse>, NotFound>> CompareAsync(Guid companyId, Guid id, int number, BriefingService service, CancellationToken ct)
    {
        var changes = await service.CompareAsync(companyId, id, number, ct);
        return changes is null ? TypedResults.NotFound() : TypedResults.Ok(changes);
    }

    private static async Task<Results<Ok<IReadOnlyList<InvestigationCandidateResponse>>, NotFound>> NewerAsync(Guid companyId, Guid id, BriefingService service, CancellationToken ct)
    {
        var candidates = await service.NewerAsync(companyId, id, ct);
        return candidates is null ? TypedResults.NotFound() : TypedResults.Ok<IReadOnlyList<InvestigationCandidateResponse>>(candidates.Select(item => new InvestigationCandidateResponse(
            item.Id, item.Title, item.Origin, item.Purpose.ToString(), item.Topics, item.MaterialUpdatedAt)).ToArray());
    }
}

public sealed record InvestigationCandidateResponse(Guid Id, string Title, string Origin, string Purpose,
    IReadOnlyList<string> Topics, DateTimeOffset MaterialUpdatedAt);
