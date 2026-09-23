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

        app.MapGet("/api/settings/ai-models", GetAiModelsAsync)
            .WithTags("Settings")
            .WithName("GetAiModelCatalog")
            .WithSummary("List RAVEN-compatible Gemini models and configured-project availability")
            .Produces<GeminiModelCatalogResponse>(StatusCodes.Status200OK);

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

    private static async Task<Ok<GeminiModelCatalogResponse>> GetAiModelsAsync(
        IGeminiModelCatalog catalog,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalog.GetAsync(cancellationToken));

    private static async Task<Results<Ok<ResearchSettingsResponse>, ValidationProblem>> UpdateAsync(
        UpdateResearchSettingsRequest request,
        IResearchSettingsService settings,
        IRuntimeModelPreferences runtimeModels,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await settings.UpdateAsync(request, cancellationToken);
            // Preserve the compatibility seam while settings remain durable.
            runtimeModels.TryUpdate(new UpdateRuntimeModelPreferencesRequest(
                updated.ProfileModel,
                updated.DeepResearchModel), out _);
            return TypedResults.Ok(updated);
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
        IRuntimeModelPreferences runtimeModels,
        CancellationToken cancellationToken)
    {
        var reset = await settings.ResetAsync(cancellationToken);
        runtimeModels.TryUpdate(new UpdateRuntimeModelPreferencesRequest(
            reset.ProfileModel,
            reset.DeepResearchModel), out _);
        return TypedResults.Ok(reset);
    }
}
