using Raven.Api.Features.Research.Parsing;

namespace Raven.Api.Tests;

public sealed class MaSoThueSourceParserTests
{
    private readonly MaSoThueSourceDetector detector = new();
    private readonly IMaSoThueSourceParser parser = new MaSoThueSourceParser();

    [Theory]
    [InlineData("https://masothue.com/0101248141")]
    [InlineData("http://www.masothue.com/0101248141-cong-ty-co-phan-fpt")]
    [InlineData("https://lookup.masothue.com/company/0101248141")]
    public void Detector_accepts_public_masothue_company_urls(string url)
    {
        Assert.True(detector.IsCompanyUrl(url));
    }

    [Theory]
    [InlineData("https://masothue.com")]
    [InlineData("https://masothue.com/")]
    [InlineData("https://masothue.com/assets/app.js")]
    [InlineData("https://example.com/0101248141")]
    [InlineData("javascript:alert(1)")]
    [InlineData(null)]
    public void Detector_rejects_homepage_assets_and_unsupported_urls(string? url)
    {
        Assert.False(detector.IsCompanyUrl(url));
    }

    [Fact]
    public void Parser_extracts_stable_vietnamese_company_facts_and_activities()
    {
        const string html = """
            <main>
              <h1>CÔNG TY CỔ PHẦN FPT</h1>
              <dl>
                <dt>Tên doanh nghiệp</dt><dd>CÔNG TY CỔ PHẦN FPT</dd>
                <dt>Tên quốc tế</dt><dd>FPT CORPORATION</dd>
                <dt>Mã số thuế</dt><dd>0101248141</dd>
                <dt>Người đại diện pháp luật</dt><dd>Nguyễn Văn A</dd>
                <dt>Địa chỉ trụ sở</dt><dd>Tòa nhà FPT, Cầu Giấy, Hà Nội</dd>
                <dt>Tình trạng hoạt động</dt><dd>Đang hoạt động</dd>
              </dl>
              <h2>Ngành nghề kinh doanh</h2>
              <ul>
                <li>Lập trình máy vi tính</li>
                <li>Tư vấn máy vi tính và quản trị hệ thống máy vi tính</li>
              </ul>
              <script>Mã số thuế: 9999999999</script>
            </main>
            """;

        var facts = parser.Parse(html);

        Assert.Equal("CÔNG TY CỔ PHẦN FPT", facts.LegalName);
        Assert.Equal("0101248141", facts.TaxId);
        Assert.Equal("FPT CORPORATION", facts.InternationalName);
        Assert.Equal("Nguyễn Văn A", facts.Representative);
        Assert.Equal("Tòa nhà FPT, Cầu Giấy, Hà Nội", facts.RegisteredAddress);
        Assert.Equal("Đang hoạt động", facts.Status);
        Assert.Equal(
            [
                "Lập trình máy vi tính",
                "Tư vấn máy vi tính và quản trị hệ thống máy vi tính"
            ],
            facts.RegisteredBusinessActivities);
    }

    [Fact]
    public void Parser_supports_plain_text_and_tax_id_branch_suffix()
    {
        const string text = """
            Legal name: Example Vietnam Company
            International name: Example Vietnam Corporation
            Tax code: 0123456789-001
            Legal representative: Tran Thi B
            Registered address: 1 Example Street, Ho Chi Minh City
            Business status: Active
            Registered business activities
            1. Software publishing
            2. Computer programming
            Products and services: must not be interpreted as a registered activity
            """;

        var facts = parser.Parse(text);

        Assert.Equal("Example Vietnam Company", facts.LegalName);
        Assert.Equal("0123456789-001", facts.TaxId);
        Assert.Equal("Example Vietnam Corporation", facts.InternationalName);
        Assert.Equal("Tran Thi B", facts.Representative);
        Assert.Equal("1 Example Street, Ho Chi Minh City", facts.RegisteredAddress);
        Assert.Equal("Active", facts.Status);
        Assert.Equal(["Software publishing", "Computer programming"], facts.RegisteredBusinessActivities);
    }

    [Fact]
    public void Parser_leaves_unavailable_or_unreliable_fields_unknown()
    {
        var facts = parser.Parse("<div>Tên doanh nghiệp: Test Company</div><div>Mã số thuế: unavailable</div>");

        Assert.Equal("Test Company", facts.LegalName);
        Assert.Null(facts.TaxId);
        Assert.Null(facts.InternationalName);
        Assert.Null(facts.Representative);
        Assert.Null(facts.RegisteredAddress);
        Assert.Null(facts.Status);
        Assert.Empty(facts.RegisteredBusinessActivities);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<div><strong>Mã số thuế: 010123")]
    [InlineData("<<<not valid html>>>")]
    public void Parser_handles_empty_or_malformed_content_without_throwing(string? content)
    {
        var facts = parser.Parse(content);

        Assert.NotNull(facts);
    }
}
