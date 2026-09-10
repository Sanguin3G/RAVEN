namespace Raven.Api.Features.Companies;

/// <summary>Input for creating a stable company identity.</summary>
public sealed record CreateCompanyRequest(
    string? Name,
    string? Website,
    string? Country,
    string? LegalName = null,
    string? RegistrationNumber = null,
    string? Headquarters = null);

/// <summary>Input used to find likely existing company identities.</summary>
public sealed record CompanyMatchRequest(
    string? Name,
    string? Website,
    string? Country,
    string? LegalName = null,
    string? RegistrationNumber = null,
    string? Headquarters = null);

/// <summary>A company returned by the RAVEN API.</summary>
public sealed record CompanyResponse(
    Guid Id,
    string Name,
    string? Website,
    string? Country,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? LegalName,
    string? RegistrationNumber,
    string? Headquarters,
    DateTimeOffset? LastResearchedAt);

/// <summary>Describes how strongly an existing company matches a submitted identity.</summary>
public enum CompanyMatchStrength
{
    Exact,
    VeryStrong,
    Strong,
    Weak
}

/// <summary>A likely existing company identity and the reason it matched.</summary>
public sealed record CompanyMatchResponse(
    CompanyResponse Company,
    CompanyMatchStrength MatchStrength,
    string MatchReason);
