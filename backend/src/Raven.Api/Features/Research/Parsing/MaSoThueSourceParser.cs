using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Raven.Api.Features.Research.Parsing;

public interface IMaSoThueSourceParser
{
    MaSoThueParsedFacts Parse(string? content);
}

/// <summary>
/// Best-effort parser for stable, human-readable MaSoThue labels. It intentionally
/// avoids private endpoints and page-specific AJAX data. A parser failure never
/// invalidates the original SourceDocument.
/// </summary>
public sealed partial class MaSoThueSourceParser : IMaSoThueSourceParser
{
    private const int ShortFactLimit = 500;
    private const int MaxActivities = 50;
    private const int MaxActivityLength = 500;

    private static readonly string[] LegalNameLabels =
    [
        "Tên doanh nghiệp",
        "Ten doanh nghiep",
        "Tên chính thức",
        "Ten chinh thuc",
        "Tên công ty",
        "Ten cong ty",
        "Legal name",
        "Company legal name",
        "Business name"
    ];

    private static readonly string[] TaxIdLabels =
    [
        "Mã số thuế",
        "Ma so thue",
        "Mã số doanh nghiệp",
        "Ma so doanh nghiep",
        "Tax identification number",
        "Tax ID",
        "Tax code",
        "Business registration number"
    ];

    private static readonly string[] InternationalNameLabels =
    [
        "Tên quốc tế",
        "Ten quoc te",
        "Tên giao dịch quốc tế",
        "Ten giao dich quoc te",
        "International name",
        "International company name"
    ];

    private static readonly string[] RepresentativeLabels =
    [
        "Người đại diện pháp luật",
        "Nguoi dai dien phap luat",
        "Người đại diện theo pháp luật",
        "Nguoi dai dien theo phap luat",
        "Đại diện pháp luật",
        "Dai dien phap luat",
        "Người đại diện",
        "Nguoi dai dien",
        "Legal representative",
        "Representative"
    ];

    private static readonly string[] AddressLabels =
    [
        "Địa chỉ trụ sở",
        "Dia chi tru so",
        "Địa chỉ trụ sở chính",
        "Dia chi tru so chinh",
        "Địa chỉ đăng ký",
        "Dia chi dang ky",
        "Địa chỉ",
        "Dia chi",
        "Registered address",
        "Headquarters",
        "Office address"
    ];

    private static readonly string[] StatusLabels =
    [
        "Tình trạng hoạt động",
        "Tinh trang hoat dong",
        "Tình trạng",
        "Tinh trang",
        "Trạng thái hoạt động",
        "Trang thai hoat dong",
        "Trạng thái",
        "Trang thai",
        "Business status",
        "Status"
    ];

    private static readonly string[] ActivityLabels =
    [
        "Ngành nghề kinh doanh",
        "Nganh nghe kinh doanh",
        "Ngành nghề kinh doanh chính",
        "Nganh nghe kinh doanh chinh",
        "Ngành nghề đăng ký",
        "Nganh nghe dang ky",
        "Ngành nghề chính",
        "Nganh nghe chinh",
        "Registered business activities",
        "Registered activities",
        "Business activities",
        "Business lines"
    ];

    // These labels are not facts parsed by this component, but they delimit a
    // registered-activity list. In particular, marketed products and services
    // must remain separate from legal registration activities.
    private static readonly string[] ActivityBoundaryLabels =
    [
        "Sản phẩm và dịch vụ",
        "San pham va dich vu",
        "Sản phẩm",
        "San pham",
        "Dịch vụ",
        "Dich vu",
        "Products and services",
        "Products/services",
        "Products",
        "Services",
        "Website",
        "Thông tin liên hệ",
        "Thong tin lien he",
        "Contact"
    ];

    private static readonly string[] AllFieldLabels =
        LegalNameLabels
            .Concat(TaxIdLabels)
            .Concat(InternationalNameLabels)
            .Concat(RepresentativeLabels)
            .Concat(AddressLabels)
            .Concat(StatusLabels)
            .Concat(ActivityLabels)
            .Concat(ActivityBoundaryLabels)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public MaSoThueParsedFacts Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return MaSoThueParsedFacts.Empty;
        }

        try
        {
            var lines = ToLines(content);
            if (lines.Count == 0)
            {
                return MaSoThueParsedFacts.Empty;
            }

            var taxValue = Extract(lines, TaxIdLabels, ShortFactLimit);
            return new MaSoThueParsedFacts(
                Extract(lines, LegalNameLabels, ShortFactLimit),
                ExtractTaxId(taxValue),
                Extract(lines, InternationalNameLabels, ShortFactLimit),
                Extract(lines, RepresentativeLabels, ShortFactLimit),
                Extract(lines, AddressLabels, ShortFactLimit),
                Extract(lines, StatusLabels, ShortFactLimit),
                ExtractActivities(lines));
        }
        catch (ArgumentException)
        {
            // Keep malformed source content from failing the research run.
            return MaSoThueParsedFacts.Empty;
        }
    }

    private static string? ExtractTaxId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = TaxIdRegex().Match(value);
        return match.Success ? match.Value : null;
    }

    private static string? Extract(
        IReadOnlyList<string> lines,
        IReadOnlyList<string> labels,
        int maximumLength)
    {
        foreach (var label in labels.OrderByDescending(label => label.Length))
        {
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                if (!TryReadSameLine(line, label, out var value))
                {
                    if (!IsLabelOnly(line, label))
                    {
                        continue;
                    }

                    value = ReadFollowingValue(lines, index + 1);
                }

                value = CleanValue(value, maximumLength);
                if (!string.IsNullOrWhiteSpace(value) && !LooksLikeAnotherLabel(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractActivities(IReadOnlyList<string> lines)
    {
        var activities = new List<string>();

        foreach (var label in ActivityLabels.OrderByDescending(label => label.Length))
        {
            for (var index = 0; index < lines.Count && activities.Count < MaxActivities; index++)
            {
                var line = lines[index];
                var hasInlineValue = TryReadSameLine(line, label, out var inlineValue);
                if (!hasInlineValue && !IsLabelOnly(line, label))
                {
                    continue;
                }

                if (hasInlineValue)
                {
                    AddActivity(activities, inlineValue);
                }

                for (var next = index + 1; next < lines.Count && activities.Count < MaxActivities; next++)
                {
                    var candidate = lines[next];
                    if (IsKnownFieldLabel(candidate))
                    {
                        break;
                    }

                    // The first free-form line after a label can be the value;
                    // retain list/table rows until the next labelled field.
                    AddActivity(activities, candidate);
                }

                if (activities.Count > 0)
                {
                    return activities;
                }
            }
        }

        return activities;
    }

    private static void AddActivity(ICollection<string> activities, string? value)
    {
        var cleaned = CleanValue(value, MaxActivityLength);
        if (string.IsNullOrWhiteSpace(cleaned) || LooksLikeAnotherLabel(cleaned))
        {
            return;
        }

        cleaned = ActivityBulletRegex().Replace(cleaned, string.Empty).Trim();
        if (cleaned.Length == 0 || activities.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        activities.Add(cleaned);
    }

    private static bool TryReadSameLine(string line, string label, out string? value)
    {
        var pattern = $"^\\s*{Regex.Escape(label)}(?:\\s*[:\\-–—]\\s*|\\s+)(?<value>.+?)\\s*$";
        var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = match.Success ? match.Groups["value"].Value : null;
        return match.Success;
    }

    private static bool IsLabelOnly(string line, string label)
    {
        var normalizedLine = NormalizeLabel(line);
        var normalizedLabel = NormalizeLabel(label);
        return normalizedLine.Equals(normalizedLabel, StringComparison.Ordinal) ||
               normalizedLine.Equals($"{normalizedLabel}:", StringComparison.Ordinal) ||
               normalizedLine.Equals($"{normalizedLabel} -", StringComparison.Ordinal);
    }

    private static string? ReadFollowingValue(IReadOnlyList<string> lines, int start)
    {
        for (var index = start; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || IsKnownFieldLabel(line))
            {
                continue;
            }

            return line;
        }

        return null;
    }

    private static bool IsKnownFieldLabel(string line) =>
        AllFieldLabels.Any(label => IsLabelOnly(line, label) || TryReadSameLine(line, label, out _));

    private static bool LooksLikeAnotherLabel(string value) =>
        IsKnownFieldLabel(value) || value.EndsWith(':');

    private static string? CleanValue(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = WhitespaceRegex().Replace(WebUtility.HtmlDecode(value), " ")
            .Trim(' ', ':', '-', '–', '—');
        return cleaned.Length == 0 ? null : cleaned[..Math.Min(cleaned.Length, maximumLength)];
    }

    private static IReadOnlyList<string> ToLines(string content)
    {
        var withoutNoise = NoiseTagRegex().Replace(content, string.Empty);
        var withLineBreaks = BlockTagRegex().Replace(withoutNoise, "\n");
        var withoutTags = HtmlTagRegex().Replace(withLineBreaks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);

        return decoded
            .Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => WhitespaceRegex().Replace(line, " ").Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static string NormalizeLabel(string value)
    {
        var decomposed = value.Trim().TrimEnd(':', '-', '–', '—').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ");
    }

    [GeneratedRegex(@"(?<!\d)\d{10}(?:-\d{3})?(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex TaxIdRegex();

    [GeneratedRegex(@"^\s*(?:[•·▪◦*]|\d+[.)]|[-–—])\s*", RegexOptions.CultureInvariant)]
    private static partial Regex ActivityBulletRegex();

    [GeneratedRegex(@"<\s*(?:script|style|noscript|template)\b[^>]*>.*?<\s*/\s*(?:script|style|noscript|template)\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex NoiseTagRegex();

    [GeneratedRegex(@"<\s*/?\s*(?:br|p|div|section|article|li|tr|td|th|h[1-6]|header|footer|dt|dd|table|ul|ol)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
