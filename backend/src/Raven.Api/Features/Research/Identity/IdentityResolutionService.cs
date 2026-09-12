using System.Collections.ObjectModel;

namespace Raven.Api.Features.Research.Identity;

/// <summary>
/// Coordinates the identity preflight boundary. The knowledge resolver only
/// describes identity topology; <see cref="IdentityResolutionPolicy"/> owns
/// the workflow decision.
/// </summary>
public interface IIdentityResolutionService
{
    Task<IdentityResolutionServiceResult> ResolveAsync(
        IdentityResolutionRequest? request,
        CancellationToken cancellationToken = default);
}

public sealed record IdentityResolutionServiceResult(
    IdentityResolutionResponse? Response,
    IReadOnlyDictionary<string, string[]> ValidationErrors)
{
    public bool IsValid => ValidationErrors.Count == 0;

    public static IdentityResolutionServiceResult Valid(IdentityResolutionResponse response) =>
        new(response, EmptyErrors());

    public static IdentityResolutionServiceResult Invalid(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(null, errors);

    private static IReadOnlyDictionary<string, string[]> EmptyErrors() =>
        new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>());
}

public sealed class IdentityResolutionService(
    IIdentityKnowledgeResolver knowledgeResolver,
    IdentityResolutionPolicy policy) : IIdentityResolutionService
{
    private const string ManualTemporaryId = "user-exact";
    private const int MaxNameLength = 240;
    private const int MaxOptionalHintLength = 240;
    private const int MaxResearchHintLength = 1_000;

    public async Task<IdentityResolutionServiceResult> ResolveAsync(
        IdentityResolutionRequest? request,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return IdentityResolutionServiceResult.Invalid(errors);
        }

        var normalized = Normalize(request!);

        // This is an explicit user action: the user is confirming the exact
        // supplied identity string, not claiming that model knowledge verified
        // any company fact.
        if (normalized.ConfirmExactName)
        {
            return IdentityResolutionServiceResult.Valid(CreateExactNameResponse(normalized));
        }

        if (!normalized.GuidedRefinement && TryResolveExplicit(normalized, out var explicitResponse))
        {
            return IdentityResolutionServiceResult.Valid(explicitResponse);
        }

        if (!normalized.AllowModelKnowledge)
        {
            return IdentityResolutionServiceResult.Valid(new IdentityResolutionResponse(
                IdentityResolutionStatus.NeedsMoreInfo,
                IdentityAmbiguityType.Unclear,
                null,
                [],
                [IdentityHintKind.Website, IdentityHintKind.Country],
                "Add a website or country, or confirm the exact name to continue.",
                IdentityResolutionMethod.ModelKnowledge));
        }

        IdentityTopologyResponse topology;
        try
        {
            // One logical knowledge call per attempt. Search and crawl are not
            // dependencies of this service and therefore cannot run here.
            topology = await knowledgeResolver.ResolveAsync(normalized, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            topology = new IdentityTopologyResponse(
                IdentityQueryInterpretation.Unknown,
                [],
                [],
                Message: "RAVEN couldn't confidently resolve this organization right now.",
                Failure: new AiFailureInfo(
                    "identity_resolution_failed",
                    "The identity resolver was unavailable or returned an invalid result.",
                    Retryable: true));
        }

        return IdentityResolutionServiceResult.Valid(policy.Derive(topology, normalized));
    }

    private static IdentityResolutionResponse CreateExactNameResponse(
        IdentityResolutionRequest request)
    {
        var option = new IdentityOption(
            ManualTemporaryId,
            request.Name!,
            request.LegalName,
            request.Country,
            request.Headquarters,
            NormalizeDomain(request.Website),
            IdentityEntityType.Company,
            ParentTemporaryId: null,
            IdentityRelationshipToQuery.Exact,
            IdentityConfidence.High,
            "The exact name and hints supplied by the user.");

        return new IdentityResolutionResponse(
            IdentityResolutionStatus.Resolved,
            IdentityAmbiguityType.None,
            ManualTemporaryId,
            [option],
            [],
            "Researching the exact name you provided.",
            IdentityResolutionMethod.UserConfirmedExactInput);
    }

    private static bool TryResolveExplicit(IdentityResolutionRequest request, out IdentityResolutionResponse response)
    {
        var domain = NormalizeDomain(request.Website);
        var scopedRegistration = !string.IsNullOrWhiteSpace(request.RegistrationNumber) &&
                                 (!string.IsNullOrWhiteSpace(request.Country) || !string.IsNullOrWhiteSpace(request.Headquarters));
        if (domain is null && !scopedRegistration)
        {
            response = null!;
            return false;
        }

        var option = new IdentityOption(
            "explicit-identifier",
            request.Name!,
            request.LegalName,
            request.Country,
            request.Headquarters,
            domain,
            IdentityEntityType.Company,
            null,
            IdentityRelationshipToQuery.Exact,
            IdentityConfidence.High,
            "Identity supplied directly by the user.");
        response = new IdentityResolutionResponse(
            IdentityResolutionStatus.Resolved,
            IdentityAmbiguityType.None,
            option.TemporaryId,
            [option], [],
            "Company identity supplied directly.",
            IdentityResolutionMethod.ExplicitIdentifier);
        return true;
    }

    private static IdentityResolutionRequest Normalize(IdentityResolutionRequest request) =>
        request with
        {
            Name = request.Name!.Trim(),
            LegalName = NormalizeOptional(request.LegalName),
            Website = NormalizeOptional(request.Website),
            Country = NormalizeOptional(request.Country),
            RegistrationNumber = NormalizeOptional(request.RegistrationNumber),
            Headquarters = NormalizeOptional(request.Headquarters),
            ResearchHint = NormalizeOptional(request.ResearchHint),
            ConfirmExactName = request.ConfirmExactName,
            AllowModelKnowledge = request.AllowModelKnowledge,
            GuidedRefinement = request.GuidedRefinement
        };

    private static Dictionary<string, string[]> Validate(IdentityResolutionRequest? request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request is null)
        {
            errors["request"] = ["An identity request is required."];
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] = ["A company name is required."];
        }
        else if (request.Name.Trim().Length > MaxNameLength)
        {
            errors[nameof(request.Name)] = [$"Company name must be {MaxNameLength} characters or fewer."];
        }

        ValidateOptionalLength(errors, nameof(request.LegalName), request.LegalName, MaxOptionalHintLength);
        ValidateOptionalLength(errors, nameof(request.Website), request.Website, MaxOptionalHintLength);
        ValidateOptionalLength(errors, nameof(request.Country), request.Country, MaxOptionalHintLength);
        ValidateOptionalLength(errors, nameof(request.RegistrationNumber), request.RegistrationNumber, MaxOptionalHintLength);
        ValidateOptionalLength(errors, nameof(request.Headquarters), request.Headquarters, MaxOptionalHintLength);
        ValidateOptionalLength(errors, nameof(request.ResearchHint), request.ResearchHint, MaxResearchHintLength);

        if (!string.IsNullOrWhiteSpace(request.Website) && NormalizeDomain(request.Website) is null)
        {
            errors[nameof(request.Website)] = ["Website must be a valid HTTP(S) URL or domain."];
        }

        return errors;
    }

    private static void ValidateOptionalLength(
        IDictionary<string, string[]> errors,
        string property,
        string? value,
        int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            errors[property] = [$"{property} must be {maxLength} characters or fewer."];
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = $"https://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Host.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
