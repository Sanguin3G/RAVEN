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
        group.MapPost("/", CreateAsync).WithName("CreateBriefing").WithSummary("Generate a Briefing from selected persisted Investigations")
            .Produces<BriefingResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway);
        group.MapPost("/{id:guid}/versions", UpdateAsync).WithName("UpdateBriefing").WithSummary("Create an immutable version from existing and selected new Investigations")
            .Produces<BriefingResponse>(StatusCodes.Status201Created).Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status502BadGateway);
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

    private static async Task<IResult> CreateAsync(Guid companyId, CreateBriefingRequest request, BriefingService service, CancellationToken ct)
    {
        try
        {
            var brief = await service.CreateAsync(companyId, request, ct);
            return TypedResults.Created($"/api/companies/{companyId:D}/briefings/{brief.Id:D}", brief);
        }
        catch (ArgumentException exception) { return TypedResults.Problem(exception.Message, statusCode: 400); }
        catch (KeyNotFoundException) { return TypedResults.NotFound(); }
        catch (InvalidOperationException exception) { return TypedResults.Problem(exception.Message, statusCode: 502); }
    }

    private static async Task<IResult> UpdateAsync(Guid companyId, Guid id, UpdateBriefingRequest request, BriefingService service, CancellationToken ct)
    {
        try
        {
            var brief = await service.UpdateAsync(companyId, id, request, ct);
            return brief is null ? TypedResults.NotFound() : TypedResults.Created($"/api/companies/{companyId:D}/briefings/{id:D}/versions/{brief.CurrentVersion.VersionNumber}", brief);
        }
        catch (ArgumentException exception) { return TypedResults.Problem(exception.Message, statusCode: 400); }
        catch (DbUpdateException) { return TypedResults.Problem("The Briefing changed while this version was being saved. Reload and try again.", statusCode: 409); }
        catch (InvalidOperationException exception) { return TypedResults.Problem(exception.Message, statusCode: 502); }
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
