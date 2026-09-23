namespace Raven.Api.Features.Research.SavedArtifacts;

public static class SavedResearchArtifactValidation
{
    public const int MaxTitleLength = 500;
    public const int MaxQuestionLength = 4_000;
    public const int MaxSummaryLength = 100_000;
    public const int MaxRawResponseLength = 200_000;
    public const int MaxModelLength = 200;
    public const int MaxProviderLength = 200;
    public const int MaxObjectiveLength = 4_000;
    public const int MaxManagedResearchJobIdLength = 200;
    public const int MaxSourceDocumentIds = 500;
    public const int MaxSourceLeads = 500;
    public const int MaxSourceLeadUrlLength = 4_000;
    public const int MaxSourceLeadTitleLength = 500;
    public const int MaxSourceLeadPublisherLength = 300;
    public const int MaxSourceLeadTypeLength = 100;
    public const int MaxSourceLeadSupportsLength = 2_000;
    public const int MaxClaims = 500;
    public const int MaxClaimFieldLength = 300;
    public const int MaxClaimStatementLength = 10_000;
    public const int MaxClaimConfidenceLength = 100;
    public const int MaxClaimNotesLength = 2_000;
    public const int MaxUncertainties = 200;
    public const int MaxUncertaintyLength = 2_000;
    public const int MaxProviderMetadata = 50;
    public const int MaxProviderMetadataKeyLength = 100;
    public const int MaxProviderMetadataValueLength = 2_000;

    public static IReadOnlyList<string> Validate(SavedResearchArtifactRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        if (request.CompanyId == Guid.Empty)
        {
            errors.Add("A saved research artifact requires a company ID.");
        }

        AddRequiredTextError(errors, request.Title, "Title", MaxTitleLength);
        AddRequiredTextError(errors, request.Question, "Question", MaxQuestionLength);
        AddRequiredTextError(errors, request.Summary, "Summary", MaxSummaryLength);
        AddOptionalLengthError(errors, request.RawResponse, "Raw response", MaxRawResponseLength);

        if (!Enum.IsDefined(request.ResearchType))
        {
            errors.Add("Research type is not supported.");
        }

        if (!Enum.IsDefined(request.Origin))
        {
            errors.Add("Research origin is not supported.");
        }
        if (!Enum.IsDefined(request.Purpose)) errors.Add("Investigation purpose is not supported.");
        if (request.Topics is { Count: > 16 } || request.Topics?.Any(topic => !InvestigationTopics.All.Contains(topic)) == true)
            errors.Add("Investigation topics must use the supported vocabulary.");

        if (request.Model is not null && request.Model.Trim().Length > MaxModelLength)
        {
            errors.Add($"Model cannot exceed {MaxModelLength} characters.");
        }

        AddOptionalLengthError(errors, request.Provider, "Provider", MaxProviderLength);
        AddOptionalLengthError(errors, request.Objective, "Objective", MaxObjectiveLength);
        AddOptionalLengthError(errors, request.ManagedResearchJobId, "Managed research job ID", MaxManagedResearchJobIdLength);

        if (request.ConversationId == Guid.Empty)
        {
            errors.Add("Conversation ID must be a non-empty ID when provided.");
        }

        if (request.DeepResearchRunId == Guid.Empty)
        {
            errors.Add("Deep Research run ID must be a non-empty ID when provided.");
        }

        var sourceDocumentIds = request.SourceDocumentIds ?? [];
        if (sourceDocumentIds.Count > MaxSourceDocumentIds)
        {
            errors.Add($"At most {MaxSourceDocumentIds} source documents may be attached.");
        }

        if (sourceDocumentIds.Any(sourceDocumentId => sourceDocumentId == Guid.Empty))
        {
            errors.Add("Source document IDs must be non-empty IDs.");
        }

        var sourceLeads = request.SourceLeads ?? [];
        if (sourceLeads.Count > MaxSourceLeads)
        {
            errors.Add($"At most {MaxSourceLeads} source leads may be attached.");
        }

        var sourceLeadIds = new HashSet<Guid>();
        foreach (var sourceLead in sourceLeads)
        {
            if (sourceLead is null)
            {
                errors.Add("Source leads cannot contain null values.");
                continue;
            }

            if (sourceLead.Id == Guid.Empty || !sourceLeadIds.Add(sourceLead.Id))
            {
                errors.Add("Source lead IDs must be unique, non-empty IDs.");
            }

            AddRequiredTextError(errors, sourceLead.Url, "Source lead URL", MaxSourceLeadUrlLength);
            if (!Uri.TryCreate(sourceLead.Url?.Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
            {
                errors.Add("Source lead URLs must use HTTP or HTTPS.");
            }

            AddOptionalLengthError(errors, sourceLead.Title, "Source lead title", MaxSourceLeadTitleLength);
            AddOptionalLengthError(errors, sourceLead.Publisher, "Source lead publisher", MaxSourceLeadPublisherLength);
            AddOptionalLengthError(errors, sourceLead.SourceType, "Source lead type", MaxSourceLeadTypeLength);
            AddOptionalLengthError(errors, sourceLead.Supports, "Source lead supports", MaxSourceLeadSupportsLength);
        }

        var claims = request.Claims ?? [];
        if (claims.Count > MaxClaims)
        {
            errors.Add($"At most {MaxClaims} claims may be attached.");
        }

        foreach (var claim in claims)
        {
            if (claim is null)
            {
                errors.Add("Claims cannot contain null values.");
                continue;
            }

            AddRequiredTextError(errors, claim.Field, "Claim field", MaxClaimFieldLength);
            AddRequiredTextError(errors, claim.Statement, "Claim statement", MaxClaimStatementLength);
            AddOptionalLengthError(errors, claim.Confidence, "Claim confidence", MaxClaimConfidenceLength);
            AddOptionalLengthError(errors, claim.Notes, "Claim notes", MaxClaimNotesLength);

            foreach (var sourceLeadId in claim.SupportingSourceLeadIds ?? [])
            {
                if (!sourceLeadIds.Contains(sourceLeadId))
                {
                    errors.Add($"Claim references unknown source lead '{sourceLeadId}'.");
                }
            }
        }

        var uncertainties = request.Uncertainties ?? [];
        if (uncertainties.Count > MaxUncertainties)
        {
            errors.Add($"At most {MaxUncertainties} uncertainties may be attached.");
        }

        foreach (var uncertainty in uncertainties)
        {
            AddRequiredTextError(errors, uncertainty, "Uncertainty", MaxUncertaintyLength);
        }

        var providerMetadata = request.ProviderMetadata ?? new Dictionary<string, string>();
        if (providerMetadata.Count > MaxProviderMetadata)
        {
            errors.Add($"At most {MaxProviderMetadata} provider metadata entries may be attached.");
        }

        foreach (var entry in providerMetadata)
        {
            AddRequiredTextError(errors, entry.Key, "Provider metadata key", MaxProviderMetadataKeyLength);
            AddRequiredTextError(errors, entry.Value, "Provider metadata value", MaxProviderMetadataValueLength);
        }

        return errors;
    }

    private static void AddRequiredTextError(
        ICollection<string> errors,
        string? value,
        string fieldName,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{fieldName} is required.");
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors.Add($"{fieldName} cannot exceed {maxLength} characters.");
        }
    }

    private static void AddOptionalLengthError(
        ICollection<string> errors,
        string? value,
        string fieldName,
        int maxLength)
    {
        if (value is not null && value.Trim().Length > maxLength)
        {
            errors.Add($"{fieldName} cannot exceed {maxLength} characters.");
        }
    }
}

/// <summary>Indicates that a saved artifact request is unsafe to persist.</summary>
public sealed class SavedResearchArtifactValidationException : ArgumentException
{
    public SavedResearchArtifactValidationException(IReadOnlyList<string> errors)
        : base(string.Join(" ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
