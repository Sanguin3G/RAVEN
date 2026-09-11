using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Search;
using Raven.Api.Features.Search.Exa;
using Raven.Api.Features.Firecrawl;

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

        app.MapPut("/api/system/model-preferences", UpdateModelPreferences)
            .WithTags("System")
            .WithSummary("Set the approved runtime Gemini model preferences for this local workspace")
            .Produces<RuntimeModelPreferenceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<Ok<CrawlerStatusResponse>> GetCrawlerStatusAsync(
        ICrawlerStatusProbe crawlerStatusProbe,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await crawlerStatusProbe.CheckAsync(cancellationToken));

    private static async Task<Ok<ProviderStatusResponse>> GetProviderStatusAsync(
        IOptions<BraveSearchOptions> brave,
        IOptions<GeminiOptions> gemini,
        IOptions<ExaSearchOptions> exa,
        IOptions<FirecrawlOptions> firecrawl,
        IRuntimeModelPreferences modelPreferences,
        ICrawlerStatusProbe crawlerStatusProbe,
        CancellationToken cancellationToken)
    {
        var crawler = await crawlerStatusProbe.CheckAsync(cancellationToken);
        return TypedResults.Ok(new ProviderStatusResponse(
            new ProviderConfigurationStatus("brave", !string.IsNullOrWhiteSpace(brave.Value.ApiKey), null, null),
            new ProviderConfigurationStatus("crawl4ai-local", !string.IsNullOrWhiteSpace(crawler.Provider), crawler.Available, null),
            new ProviderConfigurationStatus("gemini", !string.IsNullOrWhiteSpace(gemini.Value.ApiKey), null, modelPreferences.Current.FastModel),
            new ProviderConfigurationStatus("exa", !string.IsNullOrWhiteSpace(exa.Value.ApiKey), null, null),
            new ProviderConfigurationStatus("firecrawl", !string.IsNullOrWhiteSpace(firecrawl.Value.ApiKey), null, null),
            modelPreferences.Current.DeepModel));
    }

    private static IResult UpdateModelPreferences(
        UpdateRuntimeModelPreferencesRequest request,
        IRuntimeModelPreferences modelPreferences)
    {
        return modelPreferences.TryUpdate(request, out var preferences)
            ? Results.Ok(preferences)
            : Results.BadRequest(new { message = "Only the approved Gemini Flash models can be selected." });
    }
}
