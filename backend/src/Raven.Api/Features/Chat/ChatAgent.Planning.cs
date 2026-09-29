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
    private string EnrichQuery(ChatAgentRequest request, string? proposed, int round)
    {
        var query = ChatText.NormalizeQuestion(proposed ?? string.Empty);
        if (query.Length == 0)
        {
            query = request.Question;
        }
        if (!query.Contains(request.Company.Name, StringComparison.OrdinalIgnoreCase)) query = $"\"{request.Company.Name}\" {query}";
        if (ExtractYears(query).Count == 0)
            foreach (var year in ExtractYears(request.Question)) query += $" {year}";
        return ChatText.Bound(ChatText.NormalizeQuestion(query), 500);
    }

    private bool HasResearchTime(Stopwatch stopwatch) =>
        stopwatch.Elapsed < TimeSpan.FromSeconds(Math.Max(1, options.TurnDeadlineSeconds - options.FinalReserveSeconds));

    private bool HasTimeForCoverage(Stopwatch stopwatch) =>
        stopwatch.Elapsed < TimeSpan.FromSeconds(Math.Max(1,
            options.TurnDeadlineSeconds - options.FinalReserveSeconds - options.PlannerTimeoutSeconds));
}
