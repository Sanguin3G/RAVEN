using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.ExternalImport;
using Raven.Api.Features.Research.SavedArtifacts;

namespace Raven.Api.Tests;

public sealed class ExternalResearchImportTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parser_accepts_structured_markdown_and_links_claims_to_source_leads()
    {
        const string markdown = """
            ## Research Summary
            FPT Software expanded its cloud services in 2026.

            ## Claims
            - Field: ProductsServices
              Claim: The company markets cloud modernization services.
              Supporting URLs: https://example.com/services, https://example.com/services.
              Notes: The page was current at retrieval.

            ## Sources
            - Title: Cloud services
              URL: https://example.com/services
              Publisher: Example Corp
              Date: 2026-01-15
              Supports: ProductsServices

            ## Uncertainties
            - Employee count was not disclosed.

            ## Suggested Follow-up
            - Check the latest annual report.
            """;

        var result = new ExternalResearchImportParser().Parse(markdown);

        Assert.Contains("expanded its cloud services", result.Summary);
        var claim = Assert.Single(result.Claims);
        var lead = Assert.Single(result.SourceLeads);
        Assert.Equal("ProductsServices", claim.Field);
        Assert.Equal("The company markets cloud modernization services.", claim.Statement);
        Assert.Equal([lead.Id], claim.SupportingSourceLeadIds);
        Assert.Equal("Example Corp", lead.Publisher);
        Assert.Equal(new DateTime(2026, 1, 15), lead.PublishedAt!.Value.Date);
        Assert.Equal(["Employee count was not disclosed."], result.Uncertainties);
        Assert.Equal(["Check the latest annual report."], result.SuggestedFollowUps);

        var request = result.ToArtifactRequest(CompanyId, "Imported research", "Review cloud services");
        Assert.Equal(SavedResearchOrigin.ExternalImport, request.Origin);
        Assert.Empty(request.SourceDocumentIds!);
        Assert.Single(request.SourceLeads!);
        Assert.Single(request.Claims!);
    }

    [Fact]
    public void Parser_tolerates_missing_sections_and_keeps_cited_urls_as_leads()
    {
        const string markdown = """
            The company has entered a new market.
            Field: Markets
            Claim: It now serves Southeast Asia.
            Supporting URLs: https://news.example.test/story)
            Uncertainties
            - The exact launch date is unclear.
            """;

        var result = new ExternalResearchImportParser().Parse(markdown);

        Assert.Contains("entered a new market", result.Summary);
        var claim = Assert.Single(result.Claims);
        var lead = Assert.Single(result.SourceLeads);
        Assert.Equal("Markets", claim.Field);
        Assert.Equal("It now serves Southeast Asia.", claim.Statement);
        Assert.Equal([lead.Id], claim.SupportingSourceLeadIds);
        Assert.Equal("The exact launch date is unclear.", Assert.Single(result.Uncertainties));
    }

    [Fact]
    public void Brief_focuses_external_assistant_on_coverage_gaps_and_preserves_identity_context()
    {
        var company = new Company
        {
            Id = CompanyId,
            Name = "Example Software",
            LegalName = "Example Software JSC",
            Website = "https://example.test",
            Country = "Vietnam",
            Headquarters = "Hanoi"
        };
        var profile = new CompanyProfileCandidate
        {
            CompanyId = CompanyId,
            ResearchRunId = Guid.NewGuid(),
            DisplayName = "Example Software",
            LegalName = "Example Software JSC",
            Summary = "A software company."
        };
        var coverage = new EvidenceCoverageResponse(
            CompanyId,
            Guid.NewGuid(),
            [
                new EvidenceCoverageItem(ResearchTarget.LegalIdentity, CoverageLevel.Strong, 2, null, []),
                new EvidenceCoverageItem(ResearchTarget.Leadership, CoverageLevel.Weak, 1, null, []),
                new EvidenceCoverageItem(ResearchTarget.Markets, CoverageLevel.Missing, 0, null, [])
            ]);

        var brief = new ExternalResearchBriefGenerator().Generate(new ExternalResearchBriefRequest(
            company,
            profile,
            coverage,
            "Find current leadership and target markets."));

        Assert.Equal([ResearchTarget.Leadership, ResearchTarget.Markets], brief.FocusedTargets);
        Assert.Contains("Example Software JSC", brief.Markdown);
        Assert.Contains("https://example.test", brief.Markdown);
        Assert.Contains("Leadership", brief.Markdown);
        Assert.Contains("Markets", brief.Markdown);
        Assert.Contains("Return research notes only", brief.Markdown);
        Assert.DoesNotContain("Legal identity: confirm", brief.Markdown);
    }

    [Fact]
    public async Task External_import_artifact_keeps_claims_and_leads_separate_from_source_documents()
    {
        var lead = new ResearchSourceLead("https://example.test/about", "About", "Example");
        var service = new SavedResearchArtifactService(
            new InMemorySavedResearchArtifactStore(),
            new EmptySourceOwnershipReader(),
            new FixedClock(Now));

        var artifact = await service.CreateAsync(new SavedResearchArtifactRequest(
            CompanyId,
            "External notes",
            "Review company markets",
            "The company serves Southeast Asia.",
            SavedResearchType.Deep,
            Origin: SavedResearchOrigin.ExternalImport,
            SourceLeads: [lead],
            Claims: [new ResearchClaim("Markets", "The company serves Southeast Asia.", [lead.Id])],
            Uncertainties: ["The launch date is unclear."]));

        Assert.Equal(SavedResearchOrigin.ExternalImport, artifact.Origin);
        Assert.Empty(artifact.SourceDocumentIds);
        Assert.Equal(0, artifact.SourceCount);
        Assert.Single(artifact.SourceLeads);
        Assert.Single(artifact.Claims);
        Assert.Equal([lead.Id], Assert.Single(artifact.Claims).SupportingSourceLeadIds);
        Assert.Equal(["The launch date is unclear."], artifact.Uncertainties);
    }

    [Fact]
    public async Task Artifact_validation_rejects_claims_that_reference_unknown_source_leads()
    {
        var service = new SavedResearchArtifactService(
            new InMemorySavedResearchArtifactStore(),
            new EmptySourceOwnershipReader(),
            new FixedClock(Now));

        var exception = await Assert.ThrowsAsync<SavedResearchArtifactValidationException>(() =>
            service.CreateAsync(new SavedResearchArtifactRequest(
                CompanyId,
                "External notes",
                "Review company markets",
                "Notes",
                SavedResearchType.Deep,
                Origin: SavedResearchOrigin.ExternalImport,
                Claims: [new ResearchClaim("Markets", "Unknown", [Guid.NewGuid()])]))) ;

        Assert.Contains("unknown source lead", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedClock(DateTimeOffset value) : ISavedResearchArtifactClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }

    private sealed class EmptySourceOwnershipReader : ISourceDocumentOwnershipReader
    {
        public Task<IReadOnlySet<Guid>> GetOwnedSourceDocumentIdsAsync(
            Guid companyId,
            IReadOnlyCollection<Guid> sourceDocumentIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
    }
}
