using Microsoft.AspNetCore.Http.HttpResults;

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

        return app;
    }

    private static async Task<Ok<CrawlerStatusResponse>> GetCrawlerStatusAsync(
        ICrawlerStatusProbe crawlerStatusProbe,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await crawlerStatusProbe.CheckAsync(cancellationToken));
}
