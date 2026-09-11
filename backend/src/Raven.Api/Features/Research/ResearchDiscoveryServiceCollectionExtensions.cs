using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Search;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Settings;
using Raven.Api.Features.Firecrawl;
using Raven.Api.Features.Search.Exa;
using Raven.Api.Features.Crawling.Exa;
using Raven.Api.Features.Research.Routing;

namespace Raven.Api.Features.Research;

public static class ResearchDiscoveryServiceCollectionExtensions
{
    public static IServiceCollection AddResearchDiscovery(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.Configure<BraveSearchOptions>(configuration.GetSection(BraveSearchOptions.SectionName));
        services.PostConfigure<BraveSearchOptions>(options =>
        {
            options.ApiKey ??= configuration["BRAVE_SEARCH_API_KEY"];
        });
        services.Configure<ExaSearchOptions>(configuration.GetSection(ExaSearchOptions.SectionName));
        services.PostConfigure<ExaSearchOptions>(options =>
        {
            options.ApiKey ??= configuration[ExaSearchOptions.ApiKeyEnvironmentVariable];
        });
        services.Configure<ExaCrawlerOptions>(configuration.GetSection(ExaCrawlerOptions.SectionName));
        services.PostConfigure<ExaCrawlerOptions>(options =>
        {
            options.ApiKey ??= configuration[ExaCrawlerOptions.ApiKeyEnvironmentVariable];
        });
        services.Configure<FirecrawlOptions>(configuration.GetSection(FirecrawlOptions.SectionName));
        services.PostConfigure<FirecrawlOptions>(options =>
        {
            options.ApiKey ??= configuration[FirecrawlOptions.ApiKeyEnvironmentVariable];
        });

        services.AddHttpClient<BraveSearchProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<BraveSearchOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120));
        });
        services.AddHttpClient<ExaSearchProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ExaSearchOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120));
        });
        services.AddHttpClient<ExaCrawlerProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ExaCrawlerOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120));
        });
        services.AddHttpClient<FirecrawlSearchProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<FirecrawlOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 300));
        });
        services.AddHttpClient<Crawl4AiLocalProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<Crawl4AiLocalOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 300));
        });
        services.AddHttpClient<FirecrawlCrawlerProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<FirecrawlOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 300));
        });
        services.AddScoped<IProviderCatalog<ISearchProvider>>(serviceProvider => new ProviderCatalog<ISearchProvider>([
            serviceProvider.GetRequiredService<BraveSearchProvider>(),
            serviceProvider.GetRequiredService<ExaSearchProvider>(),
            serviceProvider.GetRequiredService<FirecrawlSearchProvider>()
        ]));
        services.AddScoped<IProviderCatalog<ICrawlerProvider>>(serviceProvider => new ProviderCatalog<ICrawlerProvider>([
            serviceProvider.GetRequiredService<Crawl4AiLocalProvider>(),
            serviceProvider.GetRequiredService<FirecrawlCrawlerProvider>(),
            serviceProvider.GetRequiredService<ExaCrawlerProvider>()
        ]));
        services.AddScoped<ISearchProvider, RoutingSearchProvider>();
        services.AddScoped<ICrawlerProvider, RoutingCrawlerProvider>();
        services.AddSingleton<SourceUrlNormalizer>();
        services.AddSingleton<SourceCandidateSelector>();
        services.AddSingleton<ISourceClassifier, SourceClassifier>();
        services.AddSingleton<ISourceAuthorityPolicy, SourceAuthorityPolicy>();
        services.AddScoped<IResearchEventWriter, EfResearchEventWriter>();
        services.AddScoped<ICompanyIdentityResolver>(serviceProvider => new GeminiCompanyIdentityResolver(
            serviceProvider.GetRequiredService<IAiModelProvider>(),
            researchSettings: serviceProvider.GetRequiredService<IResearchSettingsService>()));
        services.AddScoped<ISourceSemanticReranker>(serviceProvider => new GeminiSourceSemanticReranker(
            serviceProvider.GetRequiredService<IAiModelProvider>(),
            researchSettings: serviceProvider.GetRequiredService<IResearchSettingsService>()));
        services.AddScoped<IResearchCompanyService, ResearchCompanyService>();
        return services;
    }
}
