namespace Raven.Api.Features.Research.Parsing;

/// <summary>
/// Structured facts that can be read opportunistically from a public TopCV company page.
/// A null field means that the source did not expose a reliable value for that fact.
/// </summary>
public sealed record TopCvParsedFacts(
    string? RegistrationNumber,
    string? EmployeeCountRange,
    string? Industry,
    string? Address,
    string? Introduction)
{
    public static TopCvParsedFacts Empty { get; } = new(null, null, null, null, null);
}
