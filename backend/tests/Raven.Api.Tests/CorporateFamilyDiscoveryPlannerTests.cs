using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Tests;

public sealed class CorporateFamilyDiscoveryPlannerTests
{
    private readonly CorporateFamilyDiscoveryPlanner planner = new();

    [Theory]
    [InlineData("FPT")]
    [InlineData("Viettel")]
    [InlineData("Vingroup")]
    [InlineData("Sun Group")]
    public void Plan_uses_generic_family_queries_for_group_like_ambiguity(string name)
    {
        var queries = planner.Plan(
            new ResearchIdentityInput(name, null, null, "Vietnam", null, null, null),
            [
                Candidate("group.example", "Parent group"),
                Candidate("subsidiary.example", "Subsidiary company")
            ]);

        Assert.Equal(5, queries.Count);
        Assert.Contains(queries, query => query.Contains("group", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, query => query.Contains("subsidiaries", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, query => query.Contains("member companies", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, query => query.Contains("affiliates", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, query => query.Contains("brands", StringComparison.OrdinalIgnoreCase));
        Assert.All(queries, query => Assert.Contains("Vietnam", query, StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_uses_official_domain_for_bounded_site_queries()
    {
        var queries = planner.Plan(
            new ResearchIdentityInput("FPT", null, null, "Vietnam", null, null, null),
            [
                Candidate("fpt.com", "FPT Corporation", official: true),
                Candidate("fptsoftware.com", "FPT Software subsidiary", official: true)
            ],
            "https://www.fpt.com/");

        Assert.Equal(5, queries.Count);
        Assert.Contains(queries, query => query.StartsWith("site:fpt.com", StringComparison.Ordinal));
        Assert.Contains(queries, query => query.Contains("group companies", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, query => query.Contains("subsidiaries", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, query => query.Contains("member companies", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Plan_returns_no_queries_for_a_specific_website_or_registration_number()
    {
        var candidates = new[]
        {
            Candidate("fpt.com", "FPT Corporation", official: true),
            Candidate("fptsoftware.com", "FPT Software subsidiary", official: true)
        };

        var websiteQueries = planner.Plan(
            new ResearchIdentityInput("FPT Software", null, "https://fptsoftware.com", "Vietnam", null, null, null),
            candidates);
        var registrationQueries = planner.Plan(
            new ResearchIdentityInput("FPT", null, null, "Vietnam", "0101248141", null, null),
            candidates);

        Assert.Empty(websiteQueries);
        Assert.Empty(registrationQueries);
    }

    [Fact]
    public void Plan_does_not_start_a_family_pass_for_unambiguous_results()
    {
        var identity = new ResearchIdentityInput(
            "FPT Software",
            "FPT Software Company Limited",
            "https://fptsoftware.com",
            "Vietnam",
            "0101248141",
            "Hanoi",
            null);

        var analysis = planner.Analyze(identity, [Candidate("fptsoftware.com", "FPT Software", official: true)]);

        Assert.False(analysis.ShouldPlan);
        Assert.Empty(analysis.Signals);
        Assert.Empty(planner.Plan(identity, [Candidate("fptsoftware.com", "FPT Software", official: true)]));
    }

    [Fact]
    public void Plan_enforces_the_absolute_query_limit_and_normalizes_duplicate_templates()
    {
        var identity = new ResearchIdentityInput("Sun Property", null, null, null, null, null, null);
        var request = new CorporateFamilyDiscoveryRequest(
            identity,
            [
                Candidate("one.example", "Sun Property group"),
                Candidate("two.example", "Sun Property affiliate")
            ],
            MaximumQueries: 99);

        var queries = planner.Plan(request);

        Assert.Equal(CorporateFamilyDiscoveryPlanner.AbsoluteMaximumQueries, queries.Count);
        Assert.Equal(queries.Count, queries.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Analyze_reports_same_name_conflict_when_country_is_missing()
    {
        var analysis = planner.Analyze(
            new ResearchIdentityInput("Sun Property", null, null, null, null, null, null),
            [
                Candidate("sun-property.vn", "Sun Property"),
                Candidate("sun-property.example", "Sun Property")
            ]);

        Assert.True(analysis.ShouldPlan);
        Assert.Contains("search results span several domains", analysis.Signals);
        Assert.Contains("country was not supplied for a multi-entity search", analysis.Signals);
    }

    private static GroundingSourceCandidate Candidate(
        string domain,
        string title,
        bool official = false) => new(
        Guid.NewGuid(),
        $"https://{domain}/about",
        domain,
        title,
        title,
        SourceKind.OfficialWebsite,
        1,
        [],
        official);
}
