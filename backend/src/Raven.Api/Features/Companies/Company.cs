namespace Raven.Api.Features.Companies;

using Raven.Api.Features.Research;

public sealed class Company
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? LegalName { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Website { get; set; }
    public string? Country { get; set; }
    public string? Headquarters { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastResearchedAt { get; set; }
    /// <summary>Reversible workspace cleanup state. Archived companies retain every related record.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }
    public ICollection<ResearchRun> ResearchRuns { get; } = new List<ResearchRun>();
    public ICollection<SourceDocument> SourceDocuments { get; } = new List<SourceDocument>();
}
