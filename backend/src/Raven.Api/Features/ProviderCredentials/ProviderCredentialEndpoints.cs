using Raven.Api.Features.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Raven.Api.Features.ProviderCredentials;

public sealed record RuntimeConfigResponse(string? GoogleMapsEmbedApiKey, bool DemoMode);

public static class ProviderCredentialEndpoints
{
    public static IEndpointRouteBuilder MapProviderCredentialEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/provider-credentials")
            .WithTags("Provider Credentials")
            .RequireAuthorization(AuthServiceCollectionExtensions.AdminOnlyPolicy);

        admin.MapGet("", GetAsync)
            .WithName("GetProviderCredentialStatuses")
            .WithSummary("Return safe provider credential configuration status")
            .Produces<ProviderCredentialAdministrationResponse>(StatusCodes.Status200OK);

        admin.MapPut("/{provider}", ReplaceAsync)
            .WithName("ReplaceProviderCredential")
            .WithSummary("Replace an encrypted workspace provider credential")
            .Produces<ProviderCredentialOperationResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status503ServiceUnavailable);

        admin.MapDelete("/{provider}", RemoveAsync)
            .WithName("RemoveProviderCredentialOverride")
            .WithSummary("Remove an encrypted workspace override")
            .Produces<ProviderCredentialOperationResponse>(StatusCodes.Status200OK);

        admin.MapPost("/{provider}/test", TestAsync)
            .WithName("TestProviderCredential")
            .WithSummary("Test the effective provider configuration")
            .Produces<ProviderCredentialOperationResponse>(StatusCodes.Status200OK);

        app.MapGet("/api/runtime-config", GetRuntimeConfigAsync)
            .WithTags("Runtime Configuration")
            .WithName("GetBrowserRuntimeConfiguration")
            .WithSummary("Return only browser-safe runtime configuration")
            .Produces<RuntimeConfigResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<Ok<ProviderCredentialAdministrationResponse>> GetAsync(
        HttpContext httpContext,
        ProviderCredentialManagementService service,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await service.GetAsync(cancellationToken));
    }

    private static async Task<IResult> ReplaceAsync(
        string provider,
        ReplaceProviderCredentialRequest request,
        ProviderCredentialManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.ReplaceAsync(provider, request, cancellationToken);
            return TypedResults.Ok(new ProviderCredentialOperationResponse(true, "Credential saved."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["credential"] = [exception.Message] });
        }
        catch (ProviderCredentialStorageUnavailableException)
        {
            return TypedResults.Problem(
                "Workspace credential storage is disabled until RAVEN_CREDENTIAL_MASTER_KEY is configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Secure credential storage unavailable");
        }
    }

    private static async Task<IResult> RemoveAsync(
        string provider,
        ProviderCredentialManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var removed = await service.RemoveOverrideAsync(provider, cancellationToken);
            return TypedResults.Ok(new ProviderCredentialOperationResponse(true, removed
                ? "Workspace override removed; deployment configuration is now used if present."
                : "No workspace override was present."));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(new { message = exception.Message });
        }
    }

    private static async Task<IResult> TestAsync(
        string provider,
        ProviderCredentialManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await service.TestAsync(provider, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(new { message = exception.Message });
        }
    }

    private static async Task<Ok<RuntimeConfigResponse>> GetRuntimeConfigAsync(
        HttpContext httpContext,
        IProviderCredentialResolver credentials,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        var maps = await credentials.ResolveAsync(ProviderCredentialDefinitions.GoogleMaps, cancellationToken);
        return TypedResults.Ok(new RuntimeConfigResponse(
            maps.Configured ? maps.Value : null,
            configuration.GetValue<bool>("RAVEN_DEMO_MODE")));
    }
}
