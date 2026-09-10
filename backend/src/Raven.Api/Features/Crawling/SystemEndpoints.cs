using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Crawling;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/system/crawler-status", GetCrawlerStatusAsync)
            .WithTags("System")
            .WithName("GetCrawlerStatus")
            .WithSummary("Check Crawl4AI Local availability")
            .Produces<CrawlerStatusResponse>(StatusCodes.Status200OK);

        app.MapGet("/api/system/provider-status", GetProviderStatusAsync)
            .WithTags("System")
            .WithSummary("Return safe provider configuration and availability state")
            .Produces<ProviderStatusResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<Ok<CrawlerStatusResponse>> GetCrawlerStatusAsync(
        ICrawlerStatusProbe crawlerStatusProbe,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await crawlerStatusProbe.CheckAsync(cancellationToken));

    private static async Task<Ok<ProviderStatusResponse>> GetProviderStatusAsync(
        IOptions<BraveSearchOptions> brave,
        IOptions<GeminiOptions> gemini,
        ICrawlerStatusProbe crawlerStatusProbe,
        CancellationToken cancellationToken)
    {
        var crawler = await crawlerStatusProbe.CheckAsync(cancellationToken);
        return TypedResults.Ok(new ProviderStatusResponse(
            new ProviderConfigurationStatus("brave", !string.IsNullOrWhiteSpace(brave.Value.ApiKey), null, null),
            new ProviderConfigurationStatus("crawl4ai-local", !string.IsNullOrWhiteSpace(crawler.Provider), crawler.Available, null),
            new ProviderConfigurationStatus("gemini", !string.IsNullOrWhiteSpace(gemini.Value.ApiKey), null, gemini.Value.FastModel),
            gemini.Value.DeepModel));
    }
}
