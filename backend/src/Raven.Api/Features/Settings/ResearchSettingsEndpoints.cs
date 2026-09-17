using Microsoft.AspNetCore.Http.HttpResults;
using Raven.Api.Features.Ai;

namespace Raven.Api.Features.Settings;

public static class ResearchSettingsEndpoints
{
    public static IEndpointRouteBuilder MapResearchSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings/research", GetAsync)
            .WithTags("Settings")
            .WithSummary("Get non-secret persisted Research Intelligence settings")
            .Produces<ResearchSettingsResponse>(StatusCodes.Status200OK);

        app.MapPut("/api/settings/research", UpdateAsync)
            .WithTags("Settings")
            .WithSummary("Update non-secret persisted Research Intelligence settings")
            .Produces<ResearchSettingsResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/settings/research/reset", ResetAsync)
            .WithTags("Settings")
            .WithSummary("Restore default non-secret Research Intelligence settings")
            .Produces<ResearchSettingsResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<Ok<ResearchSettingsResponse>> GetAsync(
        IResearchSettingsService settings,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await settings.GetAsync(cancellationToken));

    private static async Task<Results<Ok<ResearchSettingsResponse>, ValidationProblem>> UpdateAsync(
        UpdateResearchSettingsRequest request,
        IResearchSettingsService settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await settings.UpdateAsync(request, cancellationToken); return TypedResults.Ok(updated);
        }
        catch (ResearchSettingsValidationException exception)
        {
            return TypedResults.ValidationProblem(exception.Errors
                .GroupBy(_ => "settings")
                .ToDictionary(group => group.Key, group => group.ToArray()));
        }
    }

    private static async Task<Ok<ResearchSettingsResponse>> ResetAsync(
        IResearchSettingsService settings,
        CancellationToken cancellationToken)
    {
        var reset = await settings.ResetAsync(cancellationToken);
        return TypedResults.Ok(reset);
    }
}
