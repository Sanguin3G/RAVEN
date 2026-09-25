using System.Text;
using System.Text.RegularExpressions;

namespace Raven.Api.Features.Chat;

/// <summary>Creates bounded, heading-aware excerpts without introducing a vector-store dependency.</summary>
public sealed partial class ChatEvidenceChunker
{
    private const int TargetCharacters = 2_400;
    private const int MaximumCharacters = 4_000;
    private const int OverlapCharacters = 300;
    private const int MaximumChunksPerSource = 3;

    public string Select(string question, IEnumerable<string> facets, string markdown)
    {
        var queryTerms = Tokens(string.Join(' ', new[] { question }.Concat(facets)));
        var years = YearRegex().Matches(question).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        var chunks = Split(markdown).Select((content, index) => new
        {
            Content = content,
            Index = index,
            Score = Score(content, queryTerms, years)
        });

        return string.Join("\n\n---\n\n", chunks
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(MaximumChunksPerSource)
            .OrderBy(item => item.Index)
            .Select(item => item.Content));
    }

    private static IReadOnlyList<string> Split(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        var blocks = Regex.Split(markdown.ReplaceLineEndings("\n"), "\\n{2,}")
            .Select(block => block.Trim())
            .Where(block => block.Length > 0)
            .ToArray();
        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var block in blocks)
        {
            if (current.Length > 0 && current.Length + block.Length + 2 > TargetCharacters)
            {
                AddBounded(chunks, current.ToString());
                var overlap = current.Length <= OverlapCharacters
                    ? current.ToString()
                    : current.ToString(current.Length - OverlapCharacters, OverlapCharacters);
                current.Clear().Append(overlap.TrimStart()).AppendLine().AppendLine();
            }
            current.Append(block).AppendLine().AppendLine();
        }
        if (current.Length > 0) AddBounded(chunks, current.ToString());
        return chunks;
    }

    private static void AddBounded(List<string> chunks, string value)
    {
        var remaining = value.Trim();
        while (remaining.Length > MaximumCharacters)
        {
            var splitAt = remaining.LastIndexOf('\n', MaximumCharacters - 1, MaximumCharacters);
            if (splitAt < TargetCharacters) splitAt = MaximumCharacters;
            chunks.Add(remaining[..splitAt].Trim());
            var resumeAt = Math.Max(0, splitAt - OverlapCharacters);
            remaining = remaining[resumeAt..].TrimStart();
        }
        if (remaining.Length > 0) chunks.Add(remaining);
    }

    private static int Score(string content, IReadOnlySet<string> terms, IReadOnlySet<string> years)
    {
        var contentTerms = Tokens(content);
        var lexical = contentTerms.Intersect(terms).Count() * 4;
        var yearScore = years.Count(year => content.Contains(year, StringComparison.Ordinal)) * 12;
        var headingScore = content.TrimStart().StartsWith('#') ? 3 : 0;
        return lexical + yearScore + headingScore;
    }

    private static HashSet<string> Tokens(string value) => Regex
        .Split(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
        .Where(term => term.Length > 2)
        .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"\b(?:19|20)\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex YearRegex();
}
