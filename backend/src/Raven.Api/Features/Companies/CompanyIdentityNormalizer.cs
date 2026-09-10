using System.Globalization;
using System.Text;

namespace Raven.Api.Features.Companies;

internal sealed record CompanyIdentity(
    string? Name,
    string? LegalName,
    string? RegistrationNumber,
    string? WebsiteHost,
    string? Country)
{
    public bool HasMatchableValue =>
        Name is not null ||
        LegalName is not null ||
        RegistrationNumber is not null ||
        WebsiteHost is not null;
}

internal static class CompanyIdentityNormalizer
{
    public static CompanyIdentity From(CompanyMatchRequest request) =>
        new(
            NormalizeName(request.Name),
            NormalizeName(request.LegalName),
            NormalizeRegistration(request.RegistrationNumber),
            NormalizeWebsiteHost(request.Website),
            NormalizeName(request.Country));

    public static string? NormalizeRegistration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    public static string? NormalizeWebsiteHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = $"https://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    public static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim() is { Length: > 0 } normalized ? normalized : null;
    }
}
