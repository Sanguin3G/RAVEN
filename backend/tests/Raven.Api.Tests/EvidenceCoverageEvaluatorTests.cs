using System.Text.Json;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Tests;

public sealed class EvidenceCoverageEvaluatorTests
{
    private readonly EvidenceCoverageEvaluator evaluator = new();

    [Fact]
    public void No_acquired_evidence_is_missing_and_preserves_budget_truth()
    {
        var companyId = Guid.NewGuid();

        var result = evaluator.Evaluate(
            companyId,
            null,
            [],
            [ResearchTarget.Leadership],
            budgetExhausted: true);

        var item = Assert.Single(result.Items);
        Assert.Equal(CoverageLevel.Missing, item.Level);
        Assert.Equal(0, item.SupportingSourceCount);
        Assert.Contains("budget exhausted", string.Join(' ', item.Reasons), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaSoThue_structured_tax_facts_are_supported_but_not_strong_registry_evidence()
    {
        var companyId = Guid.NewGuid();
        var source = Document(
            companyId,
            SourceKind.BusinessDirectory,
            "https://masothue.com/0101248141",
            "Công ty FPT - Mã số thuế",
            "Mã số thuế: 0101248141",
            JsonSerializer.Serialize(new
            {
                LegalName = "FPT Corporation",
                TaxId = "0101248141",
                RegisteredAddress = "Hanoi"
            }));

        var result = evaluator.Evaluate(
            companyId,
            null,
            [source],
            [ResearchTarget.LegalIdentity, ResearchTarget.TaxRegistration, ResearchTarget.Locations]);

        var legal = result.Items.Single(item => item.Target == ResearchTarget.LegalIdentity);
        var tax = result.Items.Single(item => item.Target == ResearchTarget.TaxRegistration);
        var locations = result.Items.Single(item => item.Target == ResearchTarget.Locations);

        Assert.Equal(CoverageLevel.Supported, legal.Level);
        Assert.Equal(CoverageLevel.Supported, tax.Level);
        Assert.Equal(CoverageLevel.Supported, locations.Level);
        Assert.Equal(SourceKind.BusinessDirectory, tax.StrongestSourceKind);
        Assert.Contains("not an official", string.Join(' ', tax.Reasons), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registered_business_activities_do_not_fill_marketed_products()
    {
        var companyId = Guid.NewGuid();
        var source = Document(
            companyId,
            SourceKind.BusinessDirectory,
            "https://masothue.com/0101248141",
            "Company registration",
            "Ngành nghề kinh doanh: Lập trình máy vi tính",
            JsonSerializer.Serialize(new
            {
                TaxId = "0101248141",
                RegisteredBusinessActivities = new[] { "Computer programming" }
            }));

        var result = evaluator.Evaluate(
            companyId,
            null,
            [source],
            [ResearchTarget.Industry, ResearchTarget.ProductsServices]);

        Assert.Equal(CoverageLevel.Supported, result.Items.Single(item => item.Target == ResearchTarget.Industry).Level);
        Assert.Equal(CoverageLevel.Missing, result.Items.Single(item => item.Target == ResearchTarget.ProductsServices).Level);
    }

    [Fact]
    public void Official_product_page_provides_strong_products_coverage()
    {
        var companyId = Guid.NewGuid();
        var source = Document(
            companyId,
            SourceKind.OfficialWebsite,
            "https://fpt.example/products",
            "Products and solutions",
            "Our products, services, and cloud solutions support enterprise customers.");

        var item = Assert.Single(evaluator.Evaluate(
            companyId,
            null,
            [source],
            [ResearchTarget.ProductsServices]).Items);

        Assert.Equal(CoverageLevel.Strong, item.Level);
        Assert.Equal(SourceKind.OfficialWebsite, item.StrongestSourceKind);
    }

    [Fact]
    public void LinkedIn_snippet_only_is_weak_leadership_coverage()
    {
        var companyId = Guid.NewGuid();
        var source = Document(
            companyId,
            SourceKind.LinkedIn,
            "https://linkedin.com/company/example",
            "Example company leadership",
            "Leadership and company profile snippet.");

        var item = Assert.Single(evaluator.Evaluate(
            companyId,
            null,
            [source],
            [ResearchTarget.Leadership]).Items);

        Assert.Equal(CoverageLevel.Weak, item.Level);
    }

    [Fact]
    public void Different_entity_evidence_is_excluded_from_coverage()
    {
        var companyId = Guid.NewGuid();
        var source = Document(
            companyId,
            SourceKind.OfficialWebsite,
            "https://other.example/leadership",
            "Leadership",
            "Our leadership team",
            "{\"EntityRelationship\":\"DifferentEntity\"}");

        var item = Assert.Single(evaluator.Evaluate(
            companyId,
            null,
            [source],
            [ResearchTarget.Leadership]).Items);

        Assert.Equal(CoverageLevel.Missing, item.Level);
        Assert.Equal(0, item.SupportingSourceCount);
        Assert.Contains("Different-entity", string.Join(' ', item.Reasons), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parent_context_does_not_masquerade_as_direct_company_evidence()
    {
        var companyId = Guid.NewGuid();
        var source = Document(
            companyId,
            SourceKind.OfficialWebsite,
            "https://parent.example/leadership",
            "Parent group leadership",
            "Leadership and executive board",
            "{\"EntityRelationship\":\"Parent\"}");

        var item = Assert.Single(evaluator.Evaluate(
            companyId,
            null,
            [source],
            [ResearchTarget.Leadership]).Items);

        Assert.Equal(CoverageLevel.Missing, item.Level);
        Assert.Contains("related-company context", string.Join(' ', item.Reasons), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Official_and_news_leadership_sources_are_strong_and_corroborrated()
    {
        var companyId = Guid.NewGuid();
        var sources = new[]
        {
            Document(companyId, SourceKind.OfficialWebsite, "https://example.com/leadership", "Leadership", "Our CEO and executive leadership team."),
            Document(companyId, SourceKind.News, "https://news.example/ceo", "Example appoints CEO", "Example appointed its chief executive officer.")
        };

        var item = Assert.Single(evaluator.Evaluate(
            companyId,
            null,
            sources,
            [ResearchTarget.Leadership]).Items);

        Assert.Equal(CoverageLevel.Strong, item.Level);
        Assert.Equal(2, item.SupportingSourceCount);
        Assert.Contains("independent source domains", string.Join(' ', item.Reasons), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Company_and_run_filters_keep_coverage_company_scoped()
    {
        var companyId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var sources = new[]
        {
            Document(companyId, SourceKind.OfficialWebsite, "https://example.com/products", "Products", "Official products", researchRunId: runId),
            Document(Guid.NewGuid(), SourceKind.OfficialWebsite, "https://other.com/products", "Products", "Official products", researchRunId: runId),
            Document(companyId, SourceKind.OfficialWebsite, "https://example.com/products-old", "Products", "Official products", researchRunId: Guid.NewGuid())
        };

        var item = Assert.Single(evaluator.Evaluate(
            companyId,
            runId,
            sources,
            [ResearchTarget.ProductsServices]).Items);

        Assert.Equal(1, item.SupportingSourceCount);
        Assert.Equal(CoverageLevel.Strong, item.Level);
    }

    private static SourceDocument Document(
        Guid companyId,
        SourceKind sourceKind,
        string url,
        string title,
        string content,
        string? structuredFactsJson = null,
        Guid? researchRunId = null) => new()
    {
        CompanyId = companyId,
        ResearchRunId = researchRunId ?? Guid.NewGuid(),
        Url = url,
        NormalizedUrl = url,
        Title = title,
        SourceDomain = new Uri(url).Host,
        SourceKind = sourceKind,
        StructuredFactsJson = structuredFactsJson,
        RetrievedAt = DateTimeOffset.UtcNow,
        Content = content,
        ContentHash = Guid.NewGuid().ToString("N"),
        CrawlerProvider = "fixture"
    };
}
