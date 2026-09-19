namespace Raven.Api.Features.Chat;

/// <summary>Bounds crawled content to passages most relevant to the question without a paid model call.</summary>
public sealed class ChatEvidenceReranker
{
    private const int ChunkLength = 1200;
    private const int MaximumChunks = 3;
    public string Select(string question, string markdown)
    {
        var terms = Tokens(question);
        var chunks = markdown.Chunk(ChunkLength).Select(chars => new string(chars)).Where(chunk => !string.IsNullOrWhiteSpace(chunk)).ToArray();
        return string.Join("\n\n---\n\n", chunks.Select((chunk, index) => new { Chunk = chunk, Index = index, Score = Tokens(chunk).Intersect(terms).Count() }).OrderByDescending(item => item.Score).ThenBy(item => item.Index).Take(MaximumChunks).OrderBy(item => item.Index).Select(item => item.Chunk));
    }
    private static HashSet<string> Tokens(string value) => value.ToLowerInvariant().Split([' ', '\t', '\r', '\n', ',', '.', ':', ';', '?', '!'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(term => term.Length > 2).ToHashSet(StringComparer.Ordinal);
}