using Raven.Api.Features.Search;
using Raven.Api.Features.Settings;

namespace Raven.Api.Features.Research.Routing;

/// <summary>
/// Routes search calls according to the persisted provider priority. This
/// wrapper is deliberately not registered as one of the catalog entries; see
/// the integration notes on <see cref="IProviderCatalog{TProvider}"/>.
/// </summary>
public sealed class RoutingSearchProvider(
    IProviderCatalog<ISearchProvider> catalog,
    IResearchSettingsService settings) : ISearchProvider, IProviderRouteDiagnostics
{
    public const string ProviderId = "routing-search";

    public string Id => ProviderId;

    public ProviderRoute? LastRoute { get; private set; }

    public async Task<SearchResponse> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var priorities = (await settings.GetAsync(cancellationToken)).SearchProviderPriority;
        var providers = ResolveProviders(priorities);
        if (providers.Count == 0)
        {
            LastRoute = new ProviderRoute("search", string.Join(", ", priorities), null, []);
            throw ProviderRouteContext.CreateExhaustedException(
                "search",
                string.Join(", ", priorities),
                []);
        }

        var attempts = new List<ProviderRouteAttempt>();
        foreach (var provider in providers)
        {
            try
            {
                var response = await provider.SearchAsync(request, cancellationToken);
                LastRoute = new ProviderRoute("search", priorities.FirstOrDefault() ?? provider.Id, response.Provider, attempts);
                return response;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (!ProviderRouteFailureClassifier.TryClassify(exception, out var kind, out var safeMessage) ||
                    !ProviderRouteFailureClassifier.IsFallbackEligible(kind))
                {
                    // Configuration, authentication, invalid-response and
                    // unknown programming failures are intentionally visible.
                    throw;
                }

                attempts.Add(new ProviderRouteAttempt(provider.Id, kind, safeMessage));
            }
        }

        LastRoute = new ProviderRoute("search", priorities.FirstOrDefault() ?? providers[0].Id, null, attempts);
        throw ProviderRouteContext.CreateExhaustedException(
            "search",
            priorities.FirstOrDefault() ?? providers[0].Id,
            attempts);
    }

    private IReadOnlyList<ISearchProvider> ResolveProviders(IReadOnlyList<string> priorities)
    {
        var byId = catalog.Providers
            .GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return priorities
            .Where(providerId => !string.IsNullOrWhiteSpace(providerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(byId.ContainsKey)
            .Select(providerId => byId[providerId])
            .ToArray();
    }

}
