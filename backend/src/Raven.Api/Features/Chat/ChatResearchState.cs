using System.Text;
using System.Text.RegularExpressions;

namespace Raven.Api.Features.Chat;

internal sealed class ChatResearchState(string question, IEnumerable<string> requestedYears)
{
    private readonly HashSet<string> normalizedQueries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> crawledCandidateIds = new(StringComparer.Ordinal);

    public string Question { get; } = question;
    public List<string> Facets { get; } = [];
    public HashSet<string> RequestedYears { get; } = requestedYears.ToHashSet(StringComparer.Ordinal);
    public List<string> Queries { get; } = [];
    public List<ChatWebCandidate> Candidates { get; } = [];
    public List<ChatWebEvidenceDraft> WebEvidence { get; } = [];
    public List<string> ProfileExcerpts { get; } = [];
    public List<string> InvestigationExcerpts { get; } = [];
    public List<string> BriefingExcerpts { get; } = [];
    public List<string> MissingEvidence { get; } = [];
    public bool SearchAttempted { get; set; }
    public bool CrawlAttempted { get; set; }
    public bool ProviderFailureObserved { get; set; }

    public bool TryAddQuery(string query)
    {
        var normalized = Regex.Replace(query.Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0 || !normalizedQueries.Add(normalized)) return false;
        Queries.Add(query.Trim());
        return true;
    }

    public void AddCandidates(IEnumerable<ChatWebCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (Candidates.All(existing => !string.Equals(existing.NormalizedUrl, candidate.NormalizedUrl, StringComparison.Ordinal)))
                Candidates.Add(candidate);
        }
    }

    public IReadOnlyList<ChatWebCandidate> NextCandidates(int count, IReadOnlyList<string>? preferredIds = null)
    {
        var preferences = (preferredIds ?? []).Distinct(StringComparer.Ordinal).Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        return Candidates
        .Where(candidate => !crawledCandidateIds.Contains(candidate.Id))
        .OrderBy(candidate => preferences.TryGetValue(candidate.Id, out var index) ? index : int.MaxValue)
        .ThenByDescending(candidate => candidate.Score)
        .ThenBy(candidate => candidate.SearchRank)
        .Take(count)
        .ToArray();
    }

    public void MarkCrawled(string candidateId) => crawledCandidateIds.Add(candidateId);

    public bool CoversRequestedYears()
    {
        if (RequestedYears.Count == 0) return true;
        var corpus = string.Join('\n', ProfileExcerpts
            .Concat(InvestigationExcerpts)
            .Concat(BriefingExcerpts)
            .Concat(WebEvidence.Select(item => $"{item.SearchSnippet} {item.ContentExcerpt}")));
        return RequestedYears.All(year => corpus.Contains(year, StringComparison.Ordinal));
    }

    public string ToCoveragePromptText(int maximumCharacters)
    {
        var builder = new StringBuilder();
        AppendResearchSummary(builder);
        builder.AppendLine("QUERIES ALREADY RUN: " + string.Join(" | ", Queries));
        builder.AppendLine("SEARCH CANDIDATES:");
        foreach (var candidate in Candidates.Take(15))
            builder.AppendLine($"{candidate.Id} | {candidate.Title} | {candidate.NormalizedUrl} | snippet={candidate.Snippet} | score={candidate.Score} | {candidate.RankReason}");
        AppendEvidence(builder);
        return ChatText.Bound(builder.ToString(), maximumCharacters);
    }

    public string ToFinalPromptText(int maximumCharacters)
    {
        var builder = new StringBuilder();
        AppendResearchSummary(builder);
        // Search candidates and snippets are intentionally excluded. The final
        // model sees only material acquired as evidence, so it cannot cite a
        // candidate that was ranked but never crawled.
        AppendEvidence(builder);
        return ChatText.Bound(builder.ToString(), maximumCharacters);
    }

    private void AppendResearchSummary(StringBuilder builder)
    {
        builder.AppendLine("RESEARCH FACETS: " + string.Join(" | ", Facets));
        builder.AppendLine("REQUESTED YEARS: " + string.Join(", ", RequestedYears.Order()));
        builder.AppendLine("MISSING EVIDENCE: " + string.Join(" | ", MissingEvidence));
    }

    private void AppendEvidence(StringBuilder builder)
    {
        builder.AppendLine("PROFILE SOURCE EXCERPTS:");
        foreach (var excerpt in ProfileExcerpts) builder.AppendLine(excerpt);
        builder.AppendLine("ATTACHED INVESTIGATION EXCERPTS:");
        foreach (var excerpt in InvestigationExcerpts) builder.AppendLine(excerpt);
        builder.AppendLine("ATTACHED BRIEFING EXCERPTS:");
        foreach (var excerpt in BriefingExcerpts) builder.AppendLine(excerpt);
        builder.AppendLine("ACCUMULATED WEB EVIDENCE:");
        foreach (var evidence in WebEvidence.Take(8)) builder.AppendLine(evidence.ToPromptText());
    }
}
