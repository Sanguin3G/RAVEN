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
    private async Task<ChatAgentCompletion?> TryAnswerProfilePersonAsync(ChatAgentRequest request, CancellationToken cancellationToken)
    {
        var match = ChatProfilePersonPolicy.Match(request.Profile, request.Question);
        if (match is null) return null;

        var tools = new List<ChatToolExecution>();
        foreach (var sourceId in match.SourceIds)
        {
            var stopwatch = Stopwatch.StartNew();
            var excerpt = await evidenceTool.ReadExcerptAsync(request, sourceId, cancellationToken,
                [match.Leader.Name, match.Leader.Title ?? string.Empty]);
            stopwatch.Stop();
            tools.Add(new ChatToolExecution
            {
                Tool = "get_source_excerpt", Provider = "raven-db", Status = excerpt.Succeeded ? "succeeded" : "failed",
                DurationMs = stopwatch.ElapsedMilliseconds, InputSummary = sourceId.ToString(),
                OutputSummary = ChatText.Bound(excerpt.Succeeded ? excerpt.Content : excerpt.Error, 500),
                ErrorCode = excerpt.ErrorCode
            });
            if (!excerpt.Succeeded || excerpt.Content?.Contains(match.Leader.Name, StringComparison.OrdinalIgnoreCase) != true)
                continue;

            var company = request.Profile.DisplayName ?? request.Company.Name;
            var title = match.Leader.Title;
            var vietnamese = ChatProfilePersonPolicy.PreferVietnamese(request.Question, request.RecentMessages);
            var answer = vietnamese
                ? title is null
                    ? $"Company Profile đã xác nhận của {company} có ghi nhận {match.Leader.Name}, nhưng chưa nêu chức danh."
                    : $"Company Profile đã xác nhận của {company} ghi nhận {match.Leader.Name} là {title}."
                : title is null
                    ? $"The accepted Company Profile for {company} lists {match.Leader.Name}, without a role."
                    : $"The accepted Company Profile for {company} lists {match.Leader.Name} as {title}.";
            return new ChatAgentCompletion(
                new ChatAgentResult(ChatAnswerStatus.Answered, answer, [sourceId], null),
                "raven", "accepted-profile", tools);
        }
        return null;
    }

    private async Task ReadProfileEvidenceAsync(ChatAgentRequest request, IReadOnlyList<Guid> sourceIds, ChatResearchState state,
        List<ChatToolExecution> tools, CancellationToken cancellationToken)
    {
        foreach (var sourceId in sourceIds.Distinct().Take(MaxExcerptCalls))
        {
            await ReportProgressAsync(request, ChatProgressStage.CheckingProfile, "Reading accepted profile evidence", cancellationToken);
            var stopwatch = Stopwatch.StartNew();
            var result = await evidenceTool.ReadExcerptAsync(request, sourceId, cancellationToken, state.Facets);
            stopwatch.Stop();
            tools.Add(new ChatToolExecution
            {
                Tool = "get_source_excerpt", Provider = "raven-db", Status = result.Succeeded ? "succeeded" : "failed",
                DurationMs = stopwatch.ElapsedMilliseconds, InputSummary = sourceId.ToString(),
                OutputSummary = ChatText.Bound(result.Succeeded ? result.Content : result.Error, 500), ErrorCode = result.ErrorCode
            });
            if (result.Succeeded) state.ProfileExcerpts.Add(result.ToPromptText());
        }
    }

    private async Task<CoverageDecision?> TryEvaluateCoverageAsync(string model, ChatAgentRequest request, string baseContext,
        ChatResearchState state, CancellationToken cancellationToken)
    {
        try
        {
            var result = await GenerateAsync(model, CoverageSchema,
                $"{baseContext}\nQUESTION: {request.Question}\n{state.ToCoveragePromptText(28_000)}\nEvaluate whether the evidence supports every requested facet and year. Search snippets help choose pages but are not factual evidence. Return the next gap-focused query when evidence is incomplete. Do not reveal hidden reasoning.",
                CoveragePromptVersion, request.ConversationId, options.PlannerTimeoutSeconds, cancellationToken);
            return ParseCoverage(result.Json);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
