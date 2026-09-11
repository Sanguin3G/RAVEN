namespace Raven.Api.Features.Research.Parsing;

/// <summary>
/// Recognizes public MaSoThue company-directory URLs without making a network
/// request. MaSoThue is a third-party directory, not an official registry.
/// </summary>
public sealed class MaSoThueSourceDetector
{
    private const string MaSoThueHost = "masothue.com";
    private static readonly HashSet<string> StaticAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".woff", ".woff2"
    };

    public bool IsCompanyUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var candidate = url.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !IsMaSoThueHost(uri.Host))
        {
            return false;
        }

        // The home page and static host pages are not company records. Profile
        // URLs are deliberately accepted by path rather than by a brittle slug
        // grammar because MaSoThue has used more than one public URL shape.
        var path = uri.AbsolutePath.Trim('/');
        return path.Length > 0 && !StaticAssetExtensions.Contains(Path.GetExtension(path));
    }

    private static bool IsMaSoThueHost(string host)
    {
        host = host.TrimEnd('.');
        return host.Equals(MaSoThueHost, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith($".{MaSoThueHost}", StringComparison.OrdinalIgnoreCase);
    }
}
