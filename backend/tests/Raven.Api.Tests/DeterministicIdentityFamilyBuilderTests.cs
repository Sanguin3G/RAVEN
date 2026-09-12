using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Tests;

public sealed class DeterministicIdentityFamilyBuilderTests
{
    [Theory]
    [InlineData("FPT", "FPT Corporation", "FPT Software subsidiary")]
    [InlineData("Viettel", "Viettel Group", "Viettel Telecom subsidiary")]
    [InlineData("Vingroup", "Vingroup Corporation", "Vingroup Property subsidiary")]
    [InlineData("Sun Group", "Sun Group", "Sun Property subsidiary")]
    public void Builds_reviewable_parent_and_related_choices_from_generic_metadata(
        string name,
        string parentTitle,
        string relatedTitle)
    {
        var builder = new DeterministicIdentityFamilyBuilder();
        var result = builder.Build(
            new ResearchIdentityInput(name, null, null, "Vietnam", null, null, null),
            [
                Candidate("parent.example", parentTitle, official: true),
                Candidate("related.example", relatedTitle, official: true),
                Candidate("parent.example", $"About {parentTitle}", official: true)
            ]);

        Assert.True(result.Count >= 2);
        Assert.Contains(result, candidate => candidate.EntityType == GroundedEntityType.ParentGroup);
        Assert.Contains(result, candidate => candidate.EntityType == GroundedEntityType.Subsidiary);
        Assert.Single(result, candidate => candidate.Recommended);
        Assert.All(result, candidate => Assert.Contains("metadata", candidate.Rationale!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Does_not_create_choices_from_redundant_pages_on_one_domain()
    {
        var builder = new DeterministicIdentityFamilyBuilder();
        var result = builder.Build(
            new ResearchIdentityInput("FPT Software", null, null, "Vietnam", null, null, null),
            [
                Candidate("fptsoftware.com", "About FPT Software", official: true),
                Candidate("fptsoftware.com", "FPT Software leadership", official: true)
            ]);

        Assert.Empty(result);
    }

    private static GroundingSourceCandidate Candidate(string domain, string title, bool official) => new(
        Guid.NewGuid(),
        $"https://{domain}/about",
        domain,
        title,
        title,
        SourceKind.OfficialWebsite,
        1,
        ["official company domain"],
        official);
}
