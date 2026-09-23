using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Settings;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>
/// Converts a user's request into one bounded research question. It uses
/// company identity/context only; saved or prior investigations are never read.
/// </summary>
public sealed class ManagedResearchBriefPreviewService(
    IManagedResearchCompanyContextReader contextReader,
    IAiModelProvider aiProvider,
    IResearchSettingsService settings) : IManagedResearchBriefPreviewService
{
    private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromMilliseconds(500);
    private const string PromptTemplateVersion = "managed-research-question-v2";
    private static readonly JsonElement ResponseSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            question = new { type = "string", maxLength = ManagedResearchLimits.MaxObjectiveLength }
        },
        required = new[] { "question" }
    });

    public async Task<ManagedResearchBriefPreviewResponse> PreviewAsync(
        Guid companyId,
        ManagedResearchBriefPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company ID is required.", nameof(companyId));
        ArgumentNullException.ThrowIfNull(request);
        var question = ManagedResearchQuestionValidation.NormalizeRequired(request.Question, nameof(request.Question));
        var context = await contextReader.GetAsync(companyId, cancellationToken)
            ?? throw new KeyNotFoundException("The requested company was not found.");
        if (context.CompanyId != companyId) throw new InvalidOperationException("The company context does not match the requested company.");

        AiModelResult response;
        try
        {
            var configured = await settings.GetAsync(cancellationToken);
            var modelRequest = new AiModelRequest(
                configured.ChatModel,
                SystemInstruction,
                BuildPrompt(question, context),
                PromptTemplateVersion,
                ToEvidence(context),
                ResponseSchema);
            response = await aiProvider.GenerateStructuredAsync(modelRequest, cancellationToken);
            if (ShouldRetry(response.Failure))
            {
                await Task.Delay(TransientRetryDelay, cancellationToken);
                response = await aiProvider.GenerateStructuredAsync(modelRequest, cancellationToken);
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ManagedResearchBriefGenerationException();
        }

        if (!response.Succeeded || response.StructuredJson is not { } json)
        {
            throw new ManagedResearchBriefGenerationException(response.Failure);
        }

        try
        {
            return new ManagedResearchBriefPreviewResponse(
                ManagedResearchQuestionValidation.Parse(json),
                ManagedResearchBriefContext.CreateContextRevision(context));
        }
        catch (ArgumentException)
        {
            throw new ManagedResearchBriefGenerationException();
        }
    }

    private static AiEvidencePayload ToEvidence(ManagedResearchCompanyContext context) => new(
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["DisplayName"] = context.DisplayName,
            ["LegalName"] = context.LegalName,
            ["OfficialWebsite"] = context.OfficialWebsite,
            ["Country"] = context.Country,
            ["Headquarters"] = context.Headquarters,
            ["AcceptedProfileSummary"] = context.AcceptedProfileSummary
        }, []);

    private static string BuildPrompt(string question, ManagedResearchCompanyContext context) => $"""
        Rewrite the user's request as exactly one clear research question for this company.
        The approved question will be sent to a public-source research provider.

        Company identity: {context.DisplayName}
        User request: {question}

        Preserve every topic and constraint in the user's request, even when it lists many subjects. Make the company identity and the information being sought explicit. Use the same language as the user's request. Return only one question, without subquestions, headings, or answers.
        Do not state company facts, results, sources, dates, people, or conclusions as known. Do not mention models, providers, prompts, prior investigations, hidden reasoning, or this instruction.
        Treat company profile context as a navigation hint, not evidence. Do not read, reference, or rely on any existing investigation.
        """;

    private const string SystemInstruction = "You rewrite company research requests into one clear question for RAVEN. Preserve every requested topic and the user's language. Return only the requested JSON. Never fabricate factual claims or research results.";

    private static bool ShouldRetry(AiFailure? failure) =>
        failure is { Retryable: true } && failure.Code is "unavailable" or "timeout" or "rate_limited";
}

public sealed class ManagedResearchBriefGenerationException : InvalidOperationException
{
    public ManagedResearchBriefGenerationException(AiFailure? failure = null) : base(failure?.Code switch
    {
        "unavailable" => "Gemini is temporarily unavailable. Retry the research question; Deep Research has not started.",
        "timeout" => "Gemini timed out while preparing the research question. Retry; Deep Research has not started.",
        "rate_limited" => "Gemini is rate limiting research questions. Retry later; Deep Research has not started.",
        "configuration" or "authentication" => "Gemini configuration needs attention before Deep Research can start.",
        _ => "The research question could not be prepared. Retry before starting Deep Research."
    })
    {
        Code = failure?.Code is "unavailable" or "timeout" or "rate_limited" or "configuration" or "authentication"
            ? failure.Code
            : "question_generation_failed";
    }

    public string Code { get; }
}

public sealed class ManagedResearchPreviewStaleException : InvalidOperationException
{
    public ManagedResearchPreviewStaleException() : base("Company context changed. Refresh the research question before starting Deep Research.") { }
}

public static class ManagedResearchBriefContext
{
    public static string CreateContextRevision(ManagedResearchCompanyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var values = new List<string> { Normalize(context.CompanyId.ToString("D")), Normalize(context.DisplayName), Normalize(context.LegalName), Normalize(context.OfficialWebsite), Normalize(context.Country), Normalize(context.Headquarters), Normalize(context.AcceptedProfileSummary) };
        values.AddRange(context.EvidenceGaps.Select(Normalize));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001F', values))));
    }

    public static bool MatchesContextRevision(ManagedResearchCompanyContext context, string revision) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(CreateContextRevision(context)), Encoding.UTF8.GetBytes(revision.Trim()));

    private static string Normalize(string? value) => value?.Replace('\0', ' ').Trim() ?? string.Empty;
}

public static class ManagedResearchQuestionValidation
{
    public static string Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("question", out var question) || question.ValueKind != JsonValueKind.String)
        {
            throw new ManagedResearchBriefGenerationException();
        }

        return NormalizeRequired(question.GetString(), nameof(question));
    }

    public static string NormalizeRequired(string? value, string parameterName)
    {
        var normalized = value?.Replace('\0', ' ').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > ManagedResearchLimits.MaxObjectiveLength)
            throw new ArgumentException($"A non-empty question of at most {ManagedResearchLimits.MaxObjectiveLength} characters is required.", parameterName);
        return normalized;
    }
}

public static class ManagedResearchEffortResolver
{
    public static ManagedResearchEffort FromDepth(ManagedResearchDepth depth) => depth switch
    {
        ManagedResearchDepth.Focused => ManagedResearchEffort.Low,
        ManagedResearchDepth.Standard => ManagedResearchEffort.Medium,
        ManagedResearchDepth.Thorough => ManagedResearchEffort.High,
        ManagedResearchDepth.Exhaustive => ManagedResearchEffort.XHigh,
        _ => ManagedResearchEffort.Auto
    };
}
