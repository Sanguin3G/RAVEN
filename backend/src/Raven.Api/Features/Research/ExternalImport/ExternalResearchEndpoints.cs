using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.SavedArtifacts;

namespace Raven.Api.Features.Research.ExternalImport;

/// <summary>Request for preparing a provider-neutral external research brief.</summary>
public sealed record GenerateExternalResearchBriefRequest(
    string? ResearchObjective = null,
    IReadOnlyCollection<ResearchTarget>? RequestedTargets = null);

/// <summary>Human-readable external research brief returned to the workspace.</summary>
public sealed record ExternalResearchBriefResponse(
    string Objective,
    IReadOnlyList<ResearchTarget> FocusedTargets,
    string Markdown);

/// <summary>Untrusted Markdown notes pasted from an external research assistant.</summary>
public sealed record ImportExternalResearchRequest(
    string Question,
    string Markdown,
    string? Title = null,
    Guid? ConversationId = null);

/// <summary>
/// HTTP boundary for provider-neutral external research interoperability. The
/// imported result is saved as research material only; it never changes the
/// accepted Company Profile.
/// </summary>
public static class ExternalResearchEndpoints
{
    private const int MaxMarkdownLength = 200_000;

    public static IEndpointRouteBuilder MapExternalResearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/companies/{companyId:guid}/external-research/brief", GenerateBriefAsync)
            .WithTags("External Research")
            .WithName("GenerateExternalResearchBrief")
            .WithSummary("Prepare a focused research brief for an external assistant")
            .WithDescription("Builds a provider-neutral prompt from the company's identity, accepted profile and evidence gaps. It does not call an external assistant.")
            .Produces<ExternalResearchBriefResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        app.MapPost("/api/companies/{companyId:guid}/external-research/import", ImportAsync)
            .WithTags("External Research")
            .WithName("ImportExternalResearch")
            .WithSummary("Save pasted external research as reviewable notes")
            .WithDescription("Parses pasted Markdown into an untrusted investigation artifact. URLs remain source leads and are not accepted profile evidence.")
            .Produces<SavedResearchArtifactResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/companies/{companyId:guid}/external-research/import/preview", PreviewAsync)
            .WithTags("External Research")
            .WithName("PreviewExternalResearchImport")
            .WithSummary("Preview pasted external research without saving it")
            .Produces<ExternalResearchImportResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<ExternalResearchBriefResponse>, BadRequest, NotFound>> GenerateBriefAsync(
        Guid companyId,
        GenerateExternalResearchBriefRequest request,
        ICompanyService companies,
        ICompanyProfileWorkflowService profiles,
        RavenDbContext dbContext,
        IEvidenceCoverageEvaluator coverageEvaluator,
        IExternalResearchBriefGenerator generator,
        CancellationToken cancellationToken)
    {
        var companyResponse = await companies.GetByIdAsync(companyId, cancellationToken);
        if (companyResponse is null)
        {
            return TypedResults.NotFound();
        }

        if (request.RequestedTargets is not null && request.RequestedTargets.Any(target => !Enum.IsDefined(target)))
        {
            return TypedResults.BadRequest();
        }

        var profile = await profiles.GetCurrentAsync(companyId, cancellationToken);
        var coverage = await LoadAcceptedCoverageAsync(companyId, dbContext, coverageEvaluator, cancellationToken);
        var company = new Company
        {
            Id = companyResponse.Id,
            Name = companyResponse.Name,
            LegalName = companyResponse.LegalName,
            RegistrationNumber = companyResponse.RegistrationNumber,
            Website = companyResponse.Website,
            Country = companyResponse.Country,
            Headquarters = companyResponse.Headquarters,
            CreatedAt = companyResponse.CreatedAt,
            UpdatedAt = companyResponse.UpdatedAt,
            LastResearchedAt = companyResponse.LastResearchedAt,
            ArchivedAt = companyResponse.ArchivedAt
        };

        var brief = generator.Generate(new ExternalResearchBriefRequest(
            company,
            profile,
            coverage,
            request.ResearchObjective,
            request.RequestedTargets));

        return TypedResults.Ok(new ExternalResearchBriefResponse(
            brief.Objective,
            brief.FocusedTargets,
            brief.Markdown));
    }

    private static async Task<Results<Created<SavedResearchArtifactResponse>, BadRequest, NotFound>> ImportAsync(
        Guid companyId,
        ImportExternalResearchRequest request,
        ICompanyService companies,
        IExternalResearchImportParser parser,
        ISavedResearchArtifactService artifacts,
        CancellationToken cancellationToken)
    {
        if (await companies.GetByIdAsync(companyId, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Question) ||
            string.IsNullOrWhiteSpace(request.Markdown) ||
            request.Markdown.Length > MaxMarkdownLength ||
            request.ConversationId == Guid.Empty)
        {
            return TypedResults.BadRequest();
        }

        if (!TryParse(request.Markdown, parser, out var parsed))
        {
            return TypedResults.BadRequest();
        }

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? BoundTitle(request.Question)
            : request.Title.Trim();
        var artifactRequest = parsed.ToArtifactRequest(
            companyId,
            title,
            request.Question.Trim(),
            DateTimeOffset.UtcNow,
            request.ConversationId);

        try
        {
            var artifact = await artifacts.CreateAsync(artifactRequest, cancellationToken);
            var response = SavedResearchArtifactResponse.FromEntity(artifact);
            return TypedResults.Created($"/api/companies/{companyId:D}/saved-research/{artifact.Id:D}", response);
        }
        catch (SavedResearchArtifactValidationException)
        {
            return TypedResults.BadRequest();
        }
    }

    private static async Task<Results<Ok<ExternalResearchImportResult>, BadRequest, NotFound>> PreviewAsync(
        Guid companyId,
        ImportExternalResearchRequest request,
        ICompanyService companies,
        IExternalResearchImportParser parser,
        CancellationToken cancellationToken)
    {
        if (await companies.GetByIdAsync(companyId, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Markdown) || request.Markdown.Length > MaxMarkdownLength)
        {
            return TypedResults.BadRequest();
        }

        return TryParse(request.Markdown, parser, out var parsed)
            ? TypedResults.Ok(parsed)
            : TypedResults.BadRequest();
    }

    private static bool TryParse(string markdown, IExternalResearchImportParser parser, out ExternalResearchImportResult parsed)
    {
        try
        {
            parsed = parser.Parse(markdown);
            return true;
        }
        catch (ArgumentException)
        {
            parsed = default!;
            return false;
        }
    }

    private static async Task<EvidenceCoverageResponse?> LoadAcceptedCoverageAsync(
        Guid companyId,
        RavenDbContext dbContext,
        IEvidenceCoverageEvaluator evaluator,
        CancellationToken cancellationToken)
    {
        var latest = await dbContext.CompanyProfileVersions.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return evaluator.Evaluate(companyId, null, []);
        }

        var evidenceJson = await dbContext.ProfileEvidences.AsNoTracking()
            .Where(item => item.CompanyProfileVersionId == latest.Id)
            .Select(item => item.SourceDocumentIdsJson)
            .ToListAsync(cancellationToken);
        var acceptedSourceIds = evidenceJson
            .SelectMany(DeserializeSourceIds)
            .ToHashSet();
        var sources = acceptedSourceIds.Count == 0
            ? []
            : await dbContext.SourceDocuments.AsNoTracking()
                .Where(item => item.CompanyId == companyId && acceptedSourceIds.Contains(item.Id))
                .ToListAsync(cancellationToken);
        return evaluator.Evaluate(companyId, null, sources);
    }

    private static IReadOnlyList<Guid> DeserializeSourceIds(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Guid[]>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static string BoundTitle(string question)
    {
        var normalized = question.Trim();
        const string prefix = "External research: ";
        var available = SavedResearchArtifactValidation.MaxTitleLength - prefix.Length;
        return $"{prefix}{(normalized.Length <= available ? normalized : normalized[..available])}";
    }
}
