namespace Raven.Api.Features.Research;

public sealed class SourceUrlNormalizer
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "gclid", "fbclid", "msclkid", "mc_cid", "mc_eid"
    };

    public string? Normalize(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var path = uri.AbsolutePath;
        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        var retainedQuery = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !IsTrackingParameter(part))
            .ToArray();

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            Path = path,
            Query = string.Join("&", retainedQuery),
            Fragment = string.Empty
        };

        return builder.Uri.AbsoluteUri;
    }

    private static bool IsTrackingParameter(string queryPart)
    {
        var separator = queryPart.IndexOf('=');
        var name = Uri.UnescapeDataString(separator >= 0 ? queryPart[..separator] : queryPart);
        return name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || TrackingParameters.Contains(name);
    }
}
