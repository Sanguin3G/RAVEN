using Raven.Api.Features.Crawling;
using Raven.Api.Features.Settings;

namespace Raven.Api.Features.Research.Routing;

/// <summary>
/// Routes crawl calls according to the persisted crawler priority. A false
/// CrawlResult is treated as a retrieval failure unless the provider clearly
/// returned an invalid/client request response.
/// </summary>
public sealed class RoutingCrawlerProvider(
    IProviderCatalog<ICrawlerProvider> catalog,
    IResearchSettingsService settings) : ICrawlerProvider, IProviderRouteDiagnostics
{
    public const string ProviderId = "routing-crawler";

    public string Id => ProviderId;

    public ProviderRoute? LastRoute { get; private set; }

    public async Task<CrawlResult> CrawlAsync(
        CrawlRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            LastRoute = new ProviderRoute("crawl", string.Empty, null, []);
            return new CrawlResult(
                ProviderId,
                request.Url,
                null,
                null,
                null,
                false,
                "Only absolute HTTP(S) URLs can be crawled.",
                DateTimeOffset.UtcNow);
        }

        var priorities = (await settings.GetAsync(cancellationToken)).CrawlerProviderPriority;
        var providers = ResolveProviders(priorities);
        if (providers.Count == 0)
        {
            LastRoute = new ProviderRoute("crawl", string.Join(", ", priorities), null, []);
            throw ProviderRouteContext.CreateExhaustedException(
                "crawl",
                string.Join(", ", priorities),
                []);
        }

        var attempts = new List<ProviderRouteAttempt>();
        foreach (var provider in providers)
        {
            try
            {
                var result = await provider.CrawlAsync(request, cancellationToken);
                if (result.Success)
                {
                    LastRoute = new ProviderRoute("crawl", priorities.FirstOrDefault() ?? provider.Id, result.Provider, attempts);
                    return result;
                }

                if (!ProviderRouteFailureClassifier.TryClassifyCrawlFailure(result, out var kind, out var safeMessage) ||
                    !ProviderRouteFailureClassifier.IsFallbackEligible(kind))
                {
                    LastRoute = new ProviderRoute("crawl", priorities.FirstOrDefault() ?? provider.Id, result.Provider, attempts);
                    return result;
                }

                attempts.Add(new ProviderRouteAttempt(provider.Id, kind, safeMessage));
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (!ProviderRouteFailureClassifier.TryClassify(exception, out var kind, out var safeMessage) ||
                    !ProviderRouteFailureClassifier.IsFallbackEligible(kind))
                {
                    throw;
                }

                attempts.Add(new ProviderRouteAttempt(provider.Id, kind, safeMessage));
            }
        }

        LastRoute = new ProviderRoute("crawl", priorities.FirstOrDefault() ?? providers[0].Id, null, attempts);
        throw ProviderRouteContext.CreateExhaustedException(
            "crawl",
            priorities.FirstOrDefault() ?? providers[0].Id,
            attempts);
    }

    private IReadOnlyList<ICrawlerProvider> ResolveProviders(IReadOnlyList<string> priorities)
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
