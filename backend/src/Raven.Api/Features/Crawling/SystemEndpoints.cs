using Microsoft.AspNetCore.Http.HttpResults;
using Raven.Api.Features.Ai;
using Raven.Api.Features.ProviderCredentials;
using Raven.Api.Features.Research.Events;

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

        app.MapGet("/api/system/provider-health", GetProviderHealthAsync)
            .WithTags("System")
            .WithName("GetProviderHealth")
            .WithSummary("Summarize recent provider health from real RAVEN requests")
            .WithDescription("Reads bounded persisted execution telemetry and does not make provider calls.")
            .Produces<ProviderHealthResponse>(StatusCodes.Status200OK);

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

    private static async Task<Ok<ProviderHealthResponse>> GetProviderHealthAsync(
        IProviderHealthService providerHealth,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await providerHealth.GetAsync(cancellationToken));

    private static async Task<Ok<ProviderStatusResponse>> GetProviderStatusAsync(
        IProviderCredentialResolver credentials,
        IRuntimeModelPreferences modelPreferences,
        ICrawlerStatusProbe crawlerStatusProbe,
        CancellationToken cancellationToken)
    {
        var credentialStatuses = (await credentials.GetStatusesAsync(cancellationToken))
            .ToDictionary(status => status.Provider, status => status.Configured, StringComparer.OrdinalIgnoreCase);
        var crawler = await crawlerStatusProbe.CheckAsync(cancellationToken);
        return TypedResults.Ok(new ProviderStatusResponse(
            new ProviderConfigurationStatus("brave", credentialStatuses[ProviderCredentialDefinitions.Brave], null, null),
            new ProviderConfigurationStatus("crawl4ai-local", credentialStatuses[ProviderCredentialDefinitions.Crawl4Ai], crawler.Available, null),
            new ProviderConfigurationStatus("gemini", credentialStatuses[ProviderCredentialDefinitions.Gemini], null, modelPreferences.Current.FastModel),
            new ProviderConfigurationStatus("exa", credentialStatuses[ProviderCredentialDefinitions.Exa], null, null),
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
