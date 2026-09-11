using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Tests;

public sealed class SourceTaxonomyTests
{
    private readonly SourceClassifier _classifier = new();
    private readonly SourceAuthorityPolicy _policy = new();

    [Fact]
    public void OfficialHost_IsClassifiedAsOfficialWebsite()
    {
        var result = _classifier.Classify(new(
            "https://www.fpt-is.com/about-us",
            "About FPT IS",
            "Company overview",
            "fpt-is.com"));

        Assert.Equal(SourceKind.OfficialWebsite, result.SourceKind);
        Assert.Equal("fpt-is.com", result.Domain);
        Assert.Contains("official company domain", result.RecommendationReasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfficialSubdomain_IsRecognizedAndOfficialDocumentsAreSeparated()
    {
        var result = _classifier.Classify(new(
            "https://investor.example.com/reports/annual-report-2025.pdf",
            "Annual Report 2025",
            null,
            "example.com"));

        Assert.Equal(SourceKind.OfficialDocument, result.SourceKind);
        Assert.Equal("investor.example.com", result.Domain);
    }

    [Fact]
    public void TopCv_IsRecognizedByHost()
    {
        var result = _classifier.Classify(new(
            "https://www.topcv.vn/cong-ty/fpt-is/123.html",
            "FPT IS tuyển dụng",
            "Quy mô công ty",
            "fpt-is.com"));

        Assert.Equal(SourceKind.TopCv, result.SourceKind);
        Assert.Contains("TopCV", result.RecommendationReasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LinkedIn_IsSupportingSource()
    {
        var result = _classifier.Classify(new(
            "https://www.linkedin.com/company/fpt-information-system/",
            "FPT Information System",
            null,
            "fpt-is.com"));

        Assert.Equal(SourceKind.LinkedIn, result.SourceKind);
        Assert.Contains("supporting", result.RecommendationReasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://masothue.com/0101248141", "Công ty FPT - Mã số thuế", SourceKind.BusinessDirectory)]
    [InlineData("https://registry.example.org/company/fpt", "Company registration record", SourceKind.BusinessDirectory)]
    public void DirectorySignals_AreClassifiedAsBusinessDirectory(string url, string title, SourceKind expected)
    {
        var result = _classifier.Classify(new(url, title, null));

        Assert.Equal(expected, result.SourceKind);
    }

    [Theory]
    [InlineData("https://dangkykinhdoanh.gov.vn/vn/Pages/ThongTinDoanhNghiep.aspx", "Company registration record")]
    [InlineData("https://dichvuthongtin.dkkd.gov.vn/company/0101248141", "Business registration")]
    [InlineData("https://gdt.gov.vn/wps/portal/home", "Tax administration portal")]
    public void OfficialRegistryDomains_AreClassifiedAsOfficialBusinessRegistry(string url, string title)
    {
        var result = _classifier.Classify(new(url, title, null));

        Assert.Equal(SourceKind.OfficialBusinessRegistry, result.SourceKind);
        Assert.Contains("government", result.RecommendationReasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaSoThue_IsExplicitlyIdentifiedAsThirdPartyDirectory()
    {
        var result = _classifier.Classify(new(
            "https://masothue.com/0101248141",
            "CÔNG TY CỔ PHẦN FPT",
            "Mã số thuế: 0101248141"));

        Assert.Equal(SourceKind.BusinessDirectory, result.SourceKind);
        Assert.Contains("not an official", result.RecommendationReasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfficialCompanyPage_MentioningTaxId_RemainsOfficialWebsite()
    {
        var result = _classifier.Classify(new(
            "https://fpt.com.vn/about/company",
            "FPT Corporation - Tax ID 0101248141",
            "Company registration and tax information",
            "fpt.com.vn"));

        Assert.Equal(SourceKind.OfficialWebsite, result.SourceKind);
    }

    [Theory]
    [InlineData("https://vnexpress.net/fpt-mo-rong-hoat-dong-123.html", "FPT expands operations", SourceKind.News)]
    [InlineData("https://example.org/article/fpt", "FPT company article", SourceKind.News)]
    public void NewsSignals_AreClassifiedAsNews(string url, string title, SourceKind expected)
    {
        var result = _classifier.Classify(new(url, title, null));

        Assert.Equal(expected, result.SourceKind);
    }

    [Fact]
    public void UnknownValidDomain_FallsBackToExternalWebsite()
    {
        var result = _classifier.Classify(new(
            "https://partner.example.org/customers/fpt",
            "FPT customer story",
            "",
            "fpt-is.com"));

        Assert.Equal(SourceKind.ExternalWebsite, result.SourceKind);
        Assert.Contains("supporting company context", result.RecommendationReasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingOrInvalidUrl_RemainsSearchResult()
    {
        var result = _classifier.Classify(new(null, "FPT result", "A discovery snippet"));

        Assert.Equal(SourceKind.SearchResult, result.SourceKind);
        Assert.Contains("discovery metadata", result.RecommendationReasons.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthorityPolicy_PrefersRegistryForLegalIdentityAndAddress()
    {
        Assert.True(_policy.IsPreferred(SourceField.LegalIdentity, SourceKind.OfficialBusinessRegistry, SourceKind.BusinessDirectory));
        Assert.True(_policy.IsPreferred(SourceField.TaxRegistration, SourceKind.OfficialBusinessRegistry, SourceKind.BusinessDirectory));
        Assert.True(_policy.IsPreferred(SourceField.RegisteredAddress, SourceKind.OfficialBusinessRegistry, SourceKind.BusinessDirectory));
        Assert.True(_policy.Compare(SourceField.LegalIdentity, SourceKind.BusinessDirectory, SourceKind.SearchResult) > 0);
    }

    [Fact]
    public void AuthorityPolicy_PrefersOfficialWebsiteForProductsAndLocations()
    {
        Assert.True(_policy.IsPreferred(SourceField.ProductsServices, SourceKind.OfficialWebsite, SourceKind.TopCv));
        Assert.True(_policy.IsPreferred(SourceField.OperatingLocations, SourceKind.OfficialWebsite, SourceKind.LinkedIn));
        Assert.True(_policy.IsPreferred(SourceField.ProductsServices, SourceKind.OfficialDocument, SourceKind.ExternalWebsite));
    }

    [Fact]
    public void AuthorityPolicy_UsesFieldSpecificOrderingForEmployeeScaleAndLeadership()
    {
        Assert.True(_policy.IsPreferred(SourceField.EmployeeScale, SourceKind.OfficialDocument, SourceKind.OfficialWebsite));
        Assert.True(_policy.IsPreferred(SourceField.EmployeeScale, SourceKind.TopCv, SourceKind.LinkedIn));
        Assert.True(_policy.IsPreferred(SourceField.Leadership, SourceKind.OfficialWebsite, SourceKind.News));
        Assert.True(_policy.IsPreferred(SourceField.Leadership, SourceKind.News, SourceKind.TopCv));
    }
}
