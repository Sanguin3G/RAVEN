using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Settings;

namespace Raven.Api.Features.Chat;

public sealed partial class CompanyChatAgent
{
    private static bool TryParseAndValidateFinal(JsonElement json, ChatAgentRequest request, ChatResearchState state,
        out FinalDecision final, out string validationError)
    {
        try
        {
            final = ParseFinal(json);
        }
        catch (ChatProblemException)
        {
            final = EmptyFinalDecision;
            validationError = "schema_parse_failed";
            return false;
        }
        validationError = ValidateFinal(final, request, state) ?? string.Empty;
        return validationError.Length == 0;
    }

    private static string? ValidateFinal(FinalDecision final, ChatAgentRequest request, ChatResearchState state)
    {
        var profileIds = request.Profile.Evidence.SelectMany(item => item.SourceDocumentIds).ToHashSet();
        var webIds = state.WebEvidence.Select(item => item.CandidateId).ToHashSet(StringComparer.Ordinal);
        var investigationIds = (request.Investigations ?? []).Select(item => item.Id).ToHashSet();
        var briefingIds = (request.Briefings ?? []).Select(item => item.VersionId).ToHashSet();
        if (final.ProfileCitations.Any(id => !profileIds.Contains(id))) return "unsupported_profile_citation";
        if (final.WebCitations.Any(id => !webIds.Contains(id))) return "unsupported_web_citation";
        if (final.InvestigationCitations.Any(id => !investigationIds.Contains(id))) return "unsupported_investigation_citation";
        if (final.BriefingCitations.Any(id => !briefingIds.Contains(id))) return "unsupported_briefing_citation";
        if (request.RequiredInvestigationId is { } required && final.Status == ChatAnswerStatus.Answered && !final.InvestigationCitations.Contains(required))
            return "required_investigation_citation_missing";
        if (string.IsNullOrWhiteSpace(final.Answer)) return "empty_answer";
        if (final.Status != ChatAnswerStatus.Answered) return null;
        if (final.ProfileCitations.Count + final.WebCitations.Count + final.InvestigationCitations.Count + final.BriefingCitations.Count == 0)
            return "answered_without_citations";
        var allowed = profileIds.Select(id => $"profile:{id:D}")
            .Concat(webIds.Select(id => $"web:{id}"))
            .Concat(investigationIds.Select(id => $"investigation:{id:D}"))
            .Concat(briefingIds.Select(id => $"briefing:{id:D}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (final.Claims.Any(claim => string.IsNullOrWhiteSpace(claim.Text))) return "empty_claim";
        if (final.Claims.Any(claim => claim.EvidenceIds.Any(id => !IsAllowedEvidenceReference(id, allowed, profileIds, webIds, investigationIds, briefingIds))))
            return "unsupported_claim_evidence";
        return null;
    }

    private void LogFinalValidationFailure(string reason, ChatAgentRequest request, ChatResearchState state, int modelCalls) =>
        logger.LogWarning(
            "Ask RAVEN final response rejected. Reason={Reason}; ConversationId={ConversationId}; AllowedProfileCitations={ProfileCount}; AllowedWebCitations={WebCount}; AllowedInvestigationCitations={InvestigationCount}; AllowedBriefingCitations={BriefingCount}; ModelCalls={ModelCalls}",
            reason, request.ConversationId, request.Profile.Evidence.SelectMany(item => item.SourceDocumentIds).Distinct().Count(),
            state.WebEvidence.Select(item => item.CandidateId).Distinct(StringComparer.Ordinal).Count(),
            (request.Investigations ?? []).Select(item => item.Id).Distinct().Count(),
            (request.Briefings ?? []).Select(item => item.VersionId).Distinct().Count(), modelCalls);

    private static PlanDecision ParsePlan(JsonElement json)
    {
        var kind = Read(json, "questionKind")?.ToLowerInvariant() switch
        {
            "company_factual" => ChatQuestionKind.CompanyFactual,
            "conversational" => ChatQuestionKind.Conversational,
            "guidance" => ChatQuestionKind.Guidance,
            "clarification_required" => ChatQuestionKind.ClarificationRequired,
            "unsupported_scope" => ChatQuestionKind.UnsupportedScope,
            _ => throw InvalidResponse()
        };
        var queries = json.TryGetProperty("queries", out var queryArray) && queryArray.ValueKind == JsonValueKind.Array
            ? queryArray.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new PlannedQuery(Read(item, "facet") ?? string.Empty, Read(item, "query") ?? string.Empty,
                    Read(item, "sourcePreference") is "company_primary" or "independent_or_regulatory" ? Read(item, "sourcePreference")! : "neutral"))
                .Where(item => !string.IsNullOrWhiteSpace(item.Query)).Take(3).ToArray()
            : [];
        if (queries.Length == 0 && Read(json, "query") is { Length: > 0 } legacyQuery)
            queries = [new PlannedQuery("question", legacyQuery, "neutral")];
        return new(kind, Strings(json, "facets"), Strings(json, "requestedYears"), queries,
            Guids(json, "profileSourceDocumentIds", "profile:"), Read(json, "answer"), Read(json, "followUpQuestion"));
    }

    private static CoverageDecision ParseCoverage(JsonElement json)
    {
        if (!json.TryGetProperty("sufficient", out var sufficient) || sufficient.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw InvalidResponse();
        return new(sufficient.GetBoolean(), Strings(json, "missingEvidence"), Read(json, "nextQuery"), Strings(json, "candidateIds"));
    }

    private static FinalDecision ParseFinal(JsonElement json)
    {
        if (!TryStatus(Read(json, "status"), out var status)) throw InvalidResponse();
        var claims = json.TryGetProperty("claims", out var claimArray) && claimArray.ValueKind == JsonValueKind.Array
            ? claimArray.EnumerateArray().Select(item => new ClaimDecision(Read(item, "text") ?? string.Empty, Strings(item, "evidenceIds"))).ToArray()
            : [];
        return new(status, Read(json, "answer") ?? string.Empty, Guids(json, "citedSourceDocumentIds", "profile:"),
            Strings(json, "citedWebEvidenceCandidateIds").Select(value => StripPrefix(value, "web:")).ToArray(),
            Guids(json, "citedInvestigationIds", "investigation:"), Guids(json, "citedBriefingVersionIds", "briefing:"), claims,
            Strings(json, "limitations"), Read(json, "followUpQuestion"));
    }

    private static void AddExecution(List<ChatToolExecution> target, ChatWebToolExecution? value)
    {
        if (value is null) return;
        target.Add(new ChatToolExecution
        {
            Tool = value.Tool, Provider = value.Provider, Status = value.Status, DurationMs = value.DurationMs,
            InputSummary = value.InputSummary, OutputSummary = value.OutputSummary, ErrorCode = value.ErrorCode
        });
    }

    private static Task ReportProgressAsync(ChatAgentRequest request, ChatProgressStage stage, string message, CancellationToken cancellationToken) =>
        ReportProgressAsync(request, stage, message, null, null, cancellationToken);

    private static Task ReportProgressAsync(ChatAgentRequest request, ChatProgressStage stage, string message, int? completed, int? total, CancellationToken cancellationToken) =>
        request.ProgressReporter?.ReportAsync(new ChatProgressEvent(stage, message, completed, total), cancellationToken) ?? Task.CompletedTask;

    private static IReadOnlyList<string> ExtractYears(string value) => YearRegex().Matches(value).Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();
    private static bool IsYear(string value) => YearRegex().IsMatch(value) && value.Length == 4;
    private static List<Guid> Guids(JsonElement json, string name, string? prefix = null) => Strings(json, name)
        .Select(value => prefix is null ? value : StripPrefix(value, prefix))
        .Select(value => Guid.TryParse(value, out var id) ? id : throw InvalidResponse())
        .ToList();
    private static List<string> Strings(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : throw InvalidResponse()).ToList()
        : [];
    private static string? Read(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string StripPrefix(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value[prefix.Length..] : value;

    private static bool IsAllowedEvidenceReference(string value, IReadOnlySet<string> allowed,
        IReadOnlySet<Guid> profileIds, IReadOnlySet<string> webIds, IReadOnlySet<Guid> investigationIds,
        IReadOnlySet<Guid> briefingIds)
    {
        if (allowed.Contains(value)) return true;
        if (webIds.Contains(StripPrefix(value, "web:"))) return true;
        if (!Guid.TryParse(StripPrefix(value, "profile:"), out var id) &&
            !Guid.TryParse(StripPrefix(value, "investigation:"), out id) &&
            !Guid.TryParse(StripPrefix(value, "briefing:"), out id)) return false;
        return profileIds.Contains(id) || investigationIds.Contains(id) || briefingIds.Contains(id);
    }

    private static bool TryStatus(string? value, out ChatAnswerStatus status)
    {
        status = (value ?? string.Empty).ToLowerInvariant() switch
        {
            "answered" => ChatAnswerStatus.Answered,
            "conversational" => ChatAnswerStatus.Conversational,
            "guidance" => ChatAnswerStatus.Guidance,
            "clarification_required" => ChatAnswerStatus.ClarificationRequired,
            "insufficient_evidence" => ChatAnswerStatus.InsufficientEvidence,
            "unsupported_scope" => ChatAnswerStatus.UnsupportedScope,
            _ => default
        };
        return (value ?? string.Empty).ToLowerInvariant() is "answered" or "conversational" or "guidance" or "clarification_required" or "insufficient_evidence" or "unsupported_scope";
    }

    private static ChatProblemException InvalidResponse() => new(StatusCodes.Status502BadGateway, "ai_invalid_response", "Invalid AI response", "The chat provider returned an unsupported structured response.");
    private static ChatProblemException ProviderFailure(AiFailure? failure)
    {
        var code = failure?.Code ?? "provider_error";
        return new(code == "rate_limited" ? 429 : 503, $"ai_provider_{code}", "Chat provider unavailable", failure?.Message ?? "The chat provider did not return a structured result.");
    }
}
