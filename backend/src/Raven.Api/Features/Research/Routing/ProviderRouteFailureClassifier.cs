using Raven.Api.Features.Crawling;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Research.Routing;

internal static class ProviderRouteFailureClassifier
{
    public static bool TryClassify(
        Exception exception,
        out ProviderRouteFailureKind kind,
        out string? safeMessage)
    {
        if (exception is ProviderException providerException)
        {
            kind = providerException.Kind switch
            {
                ProviderFailureKind.RateLimited => ProviderRouteFailureKind.RateLimited,
                ProviderFailureKind.Timeout => ProviderRouteFailureKind.Timeout,
                ProviderFailureKind.Unavailable => ProviderRouteFailureKind.Unavailable,
                ProviderFailureKind.Configuration => ProviderRouteFailureKind.Configuration,
                ProviderFailureKind.Authentication => ProviderRouteFailureKind.Authentication,
                ProviderFailureKind.InvalidResponse => ProviderRouteFailureKind.InvalidResponse,
                _ => ProviderRouteFailureKind.Unknown
            };

            safeMessage = SafeMessage(exception.Message);
            return true;
        }

        if (exception is HttpRequestException)
        {
            kind = ProviderRouteFailureKind.Unavailable;
            safeMessage = SafeMessage(exception.Message);
            return true;
        }

        if (exception is TaskCanceledException canceled && !canceled.CancellationToken.IsCancellationRequested)
        {
            kind = ProviderRouteFailureKind.Timeout;
            safeMessage = SafeMessage(exception.Message);
            return true;
        }

        kind = ProviderRouteFailureKind.Unknown;
        safeMessage = SafeMessage(exception.Message);
        return false;
    }

    public static bool IsFallbackEligible(ProviderRouteFailureKind kind) =>
        kind is ProviderRouteFailureKind.RateLimited
            or ProviderRouteFailureKind.Timeout
            or ProviderRouteFailureKind.Unavailable
            or ProviderRouteFailureKind.RetrievalFailure;

    public static bool TryClassifyCrawlFailure(
        CrawlResult result,
        out ProviderRouteFailureKind kind,
        out string? safeMessage)
    {
        if (result.Success)
        {
            kind = ProviderRouteFailureKind.Unknown;
            safeMessage = null;
            return false;
        }

        safeMessage = SafeMessage(result.Error);
        var message = result.Error?.Trim() ?? string.Empty;

        // A provider may return a non-success CrawlResult rather than throw.
        // Preserve the same fallback policy: transient HTTP failures and
        // retrieval failures can move to the next provider, while malformed
        // requests and ordinary client errors cannot.
        if (message.Contains("Only absolute HTTP(S)", StringComparison.OrdinalIgnoreCase) ||
            ContainsClientErrorStatus(message))
        {
            kind = ProviderRouteFailureKind.InvalidRequest;
            return true;
        }

        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            kind = ProviderRouteFailureKind.Timeout;
            return true;
        }

        if (message.Contains("returned 429", StringComparison.OrdinalIgnoreCase))
        {
            kind = ProviderRouteFailureKind.RateLimited;
            return true;
        }

        if (ContainsServerErrorStatus(message) ||
            message.Contains("could not be reached", StringComparison.OrdinalIgnoreCase))
        {
            kind = ProviderRouteFailureKind.Unavailable;
            return true;
        }

        kind = ProviderRouteFailureKind.RetrievalFailure;
        return true;
    }

    private static bool ContainsClientErrorStatus(string message)
    {
        for (var status = 400; status < 500; status++)
        {
            if (status == 429)
            {
                continue;
            }

            if (message.Contains($"returned {status}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsServerErrorStatus(string message)
    {
        for (var status = 500; status < 600; status++)
        {
            if (message.Contains($"returned {status}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? SafeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var normalized = message.Trim().ReplaceLineEndings(" ");
        return normalized.Length <= 300 ? normalized : normalized[..300];
    }
}
