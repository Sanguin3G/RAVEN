namespace Raven.Api.Features.Companies;

public sealed class Company
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? LegalName { get; set; }
    public string? Website { get; set; }
    public string? Country { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastResearchedAt { get; set; }
}
