using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Raven.Api.Features.Companies;

/// <summary>Maps explicit company workspace lifecycle actions.</summary>
public static class CompanyLifecycleEndpoints
{
    /// <summary>
    /// Registers archive, restore, permanent delete, and merge routes. The
    /// integration owner should call this beside <c>MapCompanyEndpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapCompanyLifecycleEndpoints(this IEndpointRouteBuilder app)
    {
        var companies = app.MapGroup("/api/companies").WithTags("Companies");

        companies.MapPost("/{id:guid}/archive", ArchiveAsync)
            .WithName("ArchiveCompany")
            .WithSummary("Archive a company")
            .WithDescription("Hides a company from the normal workspace list while preserving all history and research data.")
            .Produces<CompanyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        companies.MapPost("/{id:guid}/restore", RestoreAsync)
            .WithName("RestoreCompany")
            .WithSummary("Restore an archived company")
            .Produces<CompanyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        companies.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteCompanyPermanently")
            .WithSummary("Permanently delete a company")
            .WithDescription("Requires an explicit confirmation payload. All dependent research, source, profile, monitoring, and investigation records are removed transactionally.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        companies.MapPost("/merge/preview", PreviewMergeAsync)
            .WithName("PreviewCompanyMerge")
            .WithSummary("Preview a company merge")
            .WithDescription("Returns affected record counts and source deduplication details without mutating either company.")
            .Produces<CompanyMergePreviewResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        companies.MapPost("/merge/confirm", ConfirmMergeAsync)
            .WithName("ConfirmCompanyMerge")
            .WithSummary("Confirm a company merge")
            .WithDescription("Reassigns related records, remaps known source provenance, deduplicates matching documents, and removes the duplicate in one transaction.")
            .Produces<CompanyMergeResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Results<Ok<CompanyResponse>, NotFound>> ArchiveAsync(
        Guid id,
        ICompanyLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        var company = await lifecycle.ArchiveAsync(id, cancellationToken);
        return company is null ? TypedResults.NotFound() : TypedResults.Ok(company);
    }

    private static async Task<Results<Ok<CompanyResponse>, NotFound>> RestoreAsync(
        Guid id,
        ICompanyLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        var company = await lifecycle.RestoreAsync(id, cancellationToken);
        return company is null ? TypedResults.NotFound() : TypedResults.Ok(company);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        [FromBody] DeleteCompanyRequest? request,
        ICompanyLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        if (request is null || !request.Confirm)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(DeleteCompanyRequest.Confirm)] = ["Permanent deletion requires explicit confirmation."]
            });
        }

        var result = await lifecycle.DeleteAsync(id, request.Confirm, cancellationToken);
        return result.Outcome switch
        {
            CompanyDeleteOutcome.NotFound => TypedResults.NotFound(),
            CompanyDeleteOutcome.ConfirmationRequired => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(DeleteCompanyRequest.Confirm)] = ["Permanent deletion requires explicit confirmation."]
            }),
            _ => TypedResults.NoContent()
        };
    }

    private static async Task<IResult> PreviewMergeAsync(
        CompanyMergePreviewRequest request,
        ICompanyLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        if (!ValidPair(request.CanonicalCompanyId, request.DuplicateCompanyId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CompanyMergePreviewRequest.CanonicalCompanyId)] = ["Canonical and duplicate IDs must be different non-empty GUIDs."]
            });
        }

        var preview = await lifecycle.PreviewMergeAsync(request, cancellationToken);
        return preview is null ? TypedResults.NotFound() : TypedResults.Ok(preview);
    }

    private static async Task<IResult> ConfirmMergeAsync(
        CompanyMergeConfirmRequest request,
        ICompanyLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        if (!ValidPair(request.CanonicalCompanyId, request.DuplicateCompanyId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CompanyMergeConfirmRequest.CanonicalCompanyId)] = ["Canonical and duplicate IDs must be different non-empty GUIDs."]
            });
        }

        if (!request.Confirm)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CompanyMergeConfirmRequest.Confirm)] = ["Company merge requires explicit confirmation."]
            });
        }

        var result = await lifecycle.ConfirmMergeAsync(request, cancellationToken);
        return result.Outcome switch
        {
            CompanyMergeOutcome.NotFound => TypedResults.NotFound(),
            CompanyMergeOutcome.Invalid or CompanyMergeOutcome.ConfirmationRequired => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CompanyMergeConfirmRequest.Confirm)] = [result.Error ?? "Company merge requires explicit confirmation."]
            }),
            _ => TypedResults.Ok(new CompanyMergeResponse(result.CanonicalCompany!, result.Preview!))
        };
    }

    private static bool ValidPair(Guid canonicalCompanyId, Guid duplicateCompanyId) =>
        canonicalCompanyId != Guid.Empty &&
        duplicateCompanyId != Guid.Empty &&
        canonicalCompanyId != duplicateCompanyId;
}

/// <summary>Successful merge response with the retained company and preview counts.</summary>
public sealed record CompanyMergeResponse(
    CompanyResponse CanonicalCompany,
    CompanyMergePreviewResponse Preview);
