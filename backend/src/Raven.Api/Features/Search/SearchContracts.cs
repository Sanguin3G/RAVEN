namespace Raven.Api.Features.Search;

public interface ISearchProvider
{
    string Id { get; }

    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}

public sealed record SearchRequest(string Query, int MaxResults, string? Country = null, string? Language = null);

public sealed record SearchResult(string Title, string Url, string? Snippet, int Rank);

public sealed record SearchResponse(string Provider, IReadOnlyList<SearchResult> Results);

public sealed class ProviderException(string provider, string message, ProviderFailureKind kind, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Provider { get; } = provider;
    public ProviderFailureKind Kind { get; } = kind;
}

public enum ProviderFailureKind
{
    Configuration,
    Authentication,
    RateLimited,
    Timeout,
    Unavailable,
    InvalidResponse
}
