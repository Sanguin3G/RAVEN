using Raven.Api.Features.Chat;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Research;
using Raven.Api.Features.Search;

namespace Raven.Api.Tests;

public sealed class ChatWebSearchPolicyAndRerankerTests
{
    [Theory]
    [InlineData("What does the company do?", true, ChatWebSearchExpectationKind.Optional)]
    [InlineData("What changed recently?", true, ChatWebSearchExpectationKind.Required)]
    [InlineData("Tìm trên web tin mới nhất", true, ChatWebSearchExpectationKind.Required)]
    [InlineData("What changed recently?", false, ChatWebSearchExpectationKind.Unavailable)]
    public void Policy_keeps_normal_questions_optional_and_requires_web_only_for_current_or_explicit_requests(string question, bool enabled, ChatWebSearchExpectationKind expected) =>
        Assert.Equal(expected, new ChatWebSearchPolicy().Evaluate(question, enabled).Kind);

    [Fact]
    public void Reranker_prefers_official_relevant_results_and_limits_each_domain()
    {
        var reranker = new ChatWebSearchReranker(new SourceUrlNormalizer());
        var ranked = reranker.Rank(new Company { Name = "Example", Website = "https://example.com" }, "Example recent investor update", [
            new SearchResult("Investor update", "https://example.com/investor/update", "Example investor update 2026", 4),
            new SearchResult("News", "https://example.com/news/one", "Example update", 1),
            new SearchResult("Press", "https://example.com/news/two", "Example update", 2),
            new SearchResult("Third", "https://example.com/news/three", "Example update", 3),
            new SearchResult("Independent report", "https://news.example.net/report", "Example investor update 2026", 1),
            new SearchResult("Login", "https://example.com/login", "Example login", 1)
        ]);
        Assert.Contains(ranked, candidate => candidate.NormalizedUrl == "https://example.com/investor/update");
        Assert.Equal(2, ranked.Count(candidate => candidate.Domain == "example.com"));
        Assert.DoesNotContain(ranked, candidate => candidate.NormalizedUrl.Contains("/login", StringComparison.Ordinal));
    }

    [Fact]
    public void Evidence_reranker_keeps_relevant_bounded_passages()
    {
        var markdown = new string('a', 1200) + " investor update revenue growth " + new string('b', 1200) + " investor update earnings " + new string('c', 1200) + " investor update outlook " + new string('d', 1200);
        var selected = new ChatEvidenceReranker().Select("investor update", markdown);
        Assert.Contains("revenue growth", selected);
        Assert.Contains("earnings", selected);
        Assert.DoesNotContain(new string('d', 1200), selected);
    }

    [Fact]
    public void Citation_policy_rejects_candidate_ids_not_read_in_the_current_turn()
    {
        var current = new ChatWebEvidenceDraft("r2", "https://example.com", "https://example.com", "Example", null, "evidence", "search", "crawl", 1, DateTimeOffset.UtcNow);
        var unsupported = ChatWebCitationPolicy.UnsupportedCurrentTurnIds(["r1", "r2", "r1"], [current]);
        Assert.Equal(["r1"], unsupported);
    }}