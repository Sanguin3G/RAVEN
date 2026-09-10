using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Search;

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
        services.AddHttpClient<ISearchProvider, BraveSearchProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<BraveSearchOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120));
        });
        services.AddHttpClient<ICrawlerProvider, Crawl4AiLocalProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<Crawl4AiLocalOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 300));
        });
        services.AddSingleton<SourceUrlNormalizer>();
        services.AddSingleton<SourceCandidateSelector>();
        services.AddScoped<IResearchCompanyService, ResearchCompanyService>();
        return services;
    }
}
