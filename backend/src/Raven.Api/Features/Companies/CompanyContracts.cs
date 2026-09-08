namespace Raven.Api.Features.Companies;

/// <summary>Input for creating a stable company identity.</summary>
public sealed record CreateCompanyRequest(string? Name, string? Website, string? Country);

/// <summary>A company returned by the RAVEN API.</summary>
public sealed record CompanyResponse(
    Guid Id,
    string Name,
    string? Website,
    string? Country,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
