namespace Raven.Api.Features.Research.Parsing;

/// <summary>
/// Recognizes public TopCV company profile URLs without making a network request.
/// </summary>
public sealed class TopCvSourceDetector
{
    private const string TopCvHost = "topcv.vn";
    private const string CompanyPath = "/cong-ty/";

    public bool IsCompanyUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !IsTopCvHost(uri.Host))
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.StartsWith(CompanyPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var slug = path[CompanyPath.Length..].Trim('/');
        return slug.Length > 0;
    }

    private static bool IsTopCvHost(string host)
    {
        host = host.TrimEnd('.');
        return host.Equals(TopCvHost, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith($".{TopCvHost}", StringComparison.OrdinalIgnoreCase);
    }
}
