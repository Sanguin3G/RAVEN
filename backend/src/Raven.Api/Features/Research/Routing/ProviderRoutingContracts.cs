using Raven.Api.Features.Crawling;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Research.Routing;

/// <summary>
/// A small DI seam that keeps the router independent from provider-specific
/// registrations. The integration root should build the catalog from the
/// concrete providers that are actually available in the application.
/// </summary>
public interface IProviderCatalog<TProvider>
    where TProvider : class
{
    IReadOnlyList<TProvider> Providers { get; }
}

public sealed class ProviderCatalog<TProvider>(IEnumerable<TProvider> providers) : IProviderCatalog<TProvider>
    where TProvider : class
{
    public IReadOnlyList<TProvider> Providers { get; } = providers
        .Where(provider => provider is not null)
        .ToArray();
}

public enum ProviderRouteFailureKind
{
    RateLimited,
    Timeout,
    Unavailable,
    RetrievalFailure,
    Configuration,
    Authentication,
    InvalidResponse,
    InvalidRequest,
    Unsupported,
    Unknown
}

/// <summary>
/// Safe, bounded route metadata intended for diagnostics and ResearchEvent
/// logging. It deliberately contains no request headers, credentials or raw
/// provider payloads.
/// </summary>
public sealed record ProviderRouteAttempt(
    string ProviderId,
    ProviderRouteFailureKind FailureKind,
    string? SafeMessage);

public sealed record ProviderRoute(
    string Capability,
    string RequestedProvider,
    string? ActualProvider,
    IReadOnlyList<ProviderRouteAttempt> Attempts);

public interface IProviderRouteDiagnostics
{
    ProviderRoute? LastRoute { get; }
}

/// <summary>
/// Raised when all providers selected by the persisted priority have failed
/// with a retryable/fallback-eligible error, or when no selected provider is
/// registered. The route metadata lets the orchestration layer log the
/// requested and attempted providers without coupling to this implementation.
/// </summary>
public static class ProviderRouteContext
{
    public const string ExceptionDataKey = "Raven.ProviderRoute";

    public static ProviderException CreateExhaustedException(
        string capability,
        string requestedProvider,
        IReadOnlyList<ProviderRouteAttempt> attempts)
    {
        var route = new ProviderRoute(capability, requestedProvider, null, attempts);
        var exception = new ProviderException(
            $"routing-{capability}",
            $"All configured {capability} providers failed with retryable errors: {string.Join(", ", attempts.Select(attempt => attempt.ProviderId))}.",
            ProviderFailureKind.Unavailable);
        Attach(exception, route);
        return exception;
    }

    public static void Attach(Exception exception, ProviderRoute route) =>
        exception.Data[ExceptionDataKey] = route;

    public static bool TryGetRoute(Exception exception, out ProviderRoute? route)
    {
        route = exception.Data[ExceptionDataKey] as ProviderRoute;
        return route is not null;
    }
}
