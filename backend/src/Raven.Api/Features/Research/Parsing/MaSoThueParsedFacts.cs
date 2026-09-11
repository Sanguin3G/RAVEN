namespace Raven.Api.Features.Research.Parsing;

/// <summary>
/// Stable, explicitly labelled facts extracted from a MaSoThue business-directory
/// page. These facts supplement the original SourceDocument; they do not turn a
/// third-party directory into an official government registry.
/// </summary>
public sealed record MaSoThueParsedFacts(
    string? LegalName,
    string? TaxId,
    string? InternationalName,
    string? Representative,
    string? RegisteredAddress,
    string? Status,
    IReadOnlyList<string> RegisteredBusinessActivities)
{
    public static MaSoThueParsedFacts Empty { get; } =
        new(null, null, null, null, null, null, Array.Empty<string>());

    /// <summary>Alias used by callers that model tax IDs as registrations.</summary>
    public string? RegistrationNumber => TaxId;

    /// <summary>Alias that makes the tax-specific meaning explicit to API callers.</summary>
    public string? TaxIdentificationNumber => TaxId;

    /// <summary>
    /// MaSoThue may list registered activities, but these are legal/business
    /// classifications and must not be treated as marketed ProductsServices.
    /// </summary>
    public IReadOnlyList<string> BusinessActivities => RegisteredBusinessActivities;
}
