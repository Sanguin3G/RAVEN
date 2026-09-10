using Raven.Api.Features.Research.Parsing;

namespace Raven.Api.Tests;

public sealed class TopCvSourceParserTests
{
    private readonly TopCvSourceDetector detector = new();
    private readonly ITopCvSourceParser parser = new TopCvSourceParser();

    [Theory]
    [InlineData("https://www.topcv.vn/cong-ty/fpt-software/123456.html")]
    [InlineData("http://topcv.vn/cong-ty/cong-ty-co-phan-vng/152928")]
    [InlineData("https://careers.topcv.vn/cong-ty/example-company")]
    public void Detector_accepts_public_topcv_company_urls(string url)
    {
        Assert.True(detector.IsCompanyUrl(url));
    }

    [Theory]
    [InlineData("https://www.topcv.vn/tim-viec-lam")]
    [InlineData("https://www.topcv.vn/cong-ty")]
    [InlineData("https://www.topcv.vn/cong-ty/")]
    [InlineData("https://www.topcv.com/cong-ty/example")]
    [InlineData("https://example.com/cong-ty/example")]
    [InlineData("javascript:alert(1)")]
    [InlineData(null)]
    public void Detector_rejects_non_company_or_unsupported_urls(string? url)
    {
        Assert.False(detector.IsCompanyUrl(url));
    }

    [Fact]
    public void Parser_extracts_labeled_vietnamese_company_facts_from_html()
    {
        const string html = """
            <section>
              <h2>Thông tin công ty</h2>
              <div><strong>Mã số thuế:</strong> 0101248141</div>
              <div><strong>Quy mô công ty:</strong> 1.000 - 5.000 nhân viên</div>
              <div><strong>Lĩnh vực hoạt động:</strong> Công nghệ thông tin</div>
              <div><strong>Địa chỉ:</strong> Tòa nhà FPT, Cầu Giấy, Hà Nội</div>
              <h3>Giới thiệu công ty</h3>
              <p>FPT Software là công ty công nghệ cung cấp dịch vụ chuyển đổi số.</p>
              <script>Mã số thuế: should-not-be-read</script>
            </section>
            """;

        var facts = parser.Parse(html);

        Assert.Equal("0101248141", facts.RegistrationNumber);
        Assert.Equal("1.000 - 5.000 nhân viên", facts.EmployeeCountRange);
        Assert.Equal("Công nghệ thông tin", facts.Industry);
        Assert.Equal("Tòa nhà FPT, Cầu Giấy, Hà Nội", facts.Address);
        Assert.Contains("FPT Software là công ty công nghệ", facts.Introduction);
    }

    [Fact]
    public void Parser_supports_english_labels_and_plain_text()
    {
        const string text = """
            Tax code: 0123456789
            Company size: 51-200 employees
            Industry: Software
            Headquarters: 1 Example Street, London
            Company introduction
            A software company building useful tools.
            """;

        var facts = parser.Parse(text);

        Assert.Equal("0123456789", facts.RegistrationNumber);
        Assert.Equal("51-200 employees", facts.EmployeeCountRange);
        Assert.Equal("Software", facts.Industry);
        Assert.Equal("1 Example Street, London", facts.Address);
        Assert.Equal("A software company building useful tools.", facts.Introduction);
    }

    [Fact]
    public void Parser_leaves_unavailable_fields_null()
    {
        var facts = parser.Parse("<div><strong>Quy mô công ty:</strong> 10-50 nhân viên</div>");

        Assert.Null(facts.RegistrationNumber);
        Assert.Equal("10-50 nhân viên", facts.EmployeeCountRange);
        Assert.Null(facts.Industry);
        Assert.Null(facts.Address);
        Assert.Null(facts.Introduction);
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
