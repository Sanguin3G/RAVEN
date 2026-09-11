namespace Raven.Api.Features.Research.SavedArtifacts;

public static class SavedResearchArtifactValidation
{
    public const int MaxTitleLength = 500;
    public const int MaxQuestionLength = 4_000;
    public const int MaxSummaryLength = 100_000;
    public const int MaxModelLength = 200;
    public const int MaxSourceDocumentIds = 500;

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

        if (!Enum.IsDefined(request.ResearchType))
        {
            errors.Add("Research type is not supported.");
        }

        if (request.Model is not null && request.Model.Trim().Length > MaxModelLength)
        {
            errors.Add($"Model cannot exceed {MaxModelLength} characters.");
        }

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
