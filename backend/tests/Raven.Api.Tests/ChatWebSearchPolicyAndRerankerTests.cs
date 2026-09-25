using Raven.Api.Features.Chat;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Research;
using Raven.Api.Features.Search;

namespace Raven.Api.Tests;

public sealed class ChatWebSearchPolicyAndRerankerTests
{
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
    public void Chunker_uses_the_original_question_and_requested_years_instead_of_the_page_title()
    {
        var markdown = "# Generic newsroom\n\n" + new string('x', 2600) +
            "\n\n## Thành tích\n\nFPT Software đạt giải thưởng quan trọng trong năm 2023, 2024 và 2025.";

        var selected = new ChatEvidenceChunker().Select(
            "Các thành tích từ năm 2023 đến năm 2025 của FPT Software là gì?", ["thành tích", "giải thưởng"], markdown);

        Assert.Contains("FPT Software", selected);
        Assert.Contains("2023", selected);
        Assert.Contains("2025", selected);
    }

    [Fact]
    public void Reranker_rewards_the_years_requested_by_this_question_without_hard_coding_current_year()
    {
        var reranker = new ChatWebSearchReranker(new SourceUrlNormalizer());
        var ranked = reranker.Rank(new Company { Name = "FPT Software", Website = "https://fptsoftware.com" },
            "FPT Software achievements 2023 2024", [
                new SearchResult("Generic update", "https://news.example.com/generic", "FPT Software update", 1),
                new SearchResult("Awards", "https://awards.example.org/fpt", "FPT Software achievements in 2023 and 2024", 4)
            ]);

        Assert.Equal("https://awards.example.org/fpt", ranked[0].NormalizedUrl);
    }

    [Fact]
    public void Reranker_uses_the_accepted_profile_website_when_the_company_record_has_no_website()
    {
        var reranker = new ChatWebSearchReranker(new SourceUrlNormalizer());
        var ranked = reranker.Rank(new Company { Name = "Masan Group Corporation" },
            "Masan subsidiaries", [
                new SearchResult("Masan business", "https://www.masangroup.com/our-business.html", "Corporate structure", 4),
                new SearchResult("Masan discussion", "https://example.org/masan", "Masan subsidiaries", 1)
            ], "https://www.masangroup.com/");

        Assert.Equal("www.masangroup.com", ranked[0].Domain);
        Assert.Contains("authoritative source", ranked[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Citation_policy_rejects_candidate_ids_not_read_in_the_current_turn()
    {
        var current = new ChatWebEvidenceDraft("r2", "https://example.com", "https://example.com", "Example", null, "evidence", "search", "crawl", 1, DateTimeOffset.UtcNow);
        var unsupported = ChatWebCitationPolicy.UnsupportedCurrentTurnIds(["r1", "r2", "r1"], [current]);
        Assert.Equal(["r1"], unsupported);
    }}
