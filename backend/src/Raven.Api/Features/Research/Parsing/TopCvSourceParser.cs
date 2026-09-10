using System.Net;
using System.Text.RegularExpressions;

namespace Raven.Api.Features.Research.Parsing;

public interface ITopCvSourceParser
{
    TopCvParsedFacts Parse(string? content);
}

/// <summary>
/// Parses stable, human-readable labels from acquired TopCV content.
/// This parser is intentionally best-effort: the raw source document remains the evidence
/// of record when a page changes or a label cannot be read.
/// </summary>
public sealed partial class TopCvSourceParser : ITopCvSourceParser
{
    private const int ShortFactLimit = 500;
    private const int IntroductionLimit = 2_000;

    private static readonly string[] RegistrationLabels =
    [
        "Mã số doanh nghiệp",
        "Mã số thuế",
        "Ma so doanh nghiep",
        "Ma so thue",
        "Registration number",
        "Tax code"
    ];

    private static readonly string[] EmployeeCountLabels =
    [
        "Quy mô công ty",
        "Quy mo cong ty",
        "Quy mô",
        "Quy mo",
        "Số lượng nhân viên",
        "So luong nhan vien",
        "Company size",
        "Employees"
    ];

    private static readonly string[] IndustryLabels =
    [
        "Ngành nghề kinh doanh",
        "Nganh nghe kinh doanh",
        "Lĩnh vực hoạt động",
        "Linh vuc hoat dong",
        "Lĩnh vực",
        "Linh vuc",
        "Industry",
        "Business field"
    ];

    private static readonly string[] AddressLabels =
    [
        "Địa chỉ trụ sở",
        "Dia chi tru so",
        "Địa chỉ",
        "Dia chi",
        "Trụ sở chính",
        "Tru so chinh",
        "Headquarters",
        "Office address"
    ];

    private static readonly string[] IntroductionLabels =
    [
        "Giới thiệu công ty",
        "Gioi thieu cong ty",
        "Giới thiệu",
        "Gioi thieu",
        "Mô tả công ty",
        "Mo ta cong ty",
        "Company introduction",
        "About company"
    ];

    public TopCvParsedFacts Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return TopCvParsedFacts.Empty;
        }

        try
        {
            var lines = ToLines(content);
            if (lines.Count == 0)
            {
                return TopCvParsedFacts.Empty;
            }

            return new TopCvParsedFacts(
                Extract(lines, RegistrationLabels, ShortFactLimit),
                Extract(lines, EmployeeCountLabels, ShortFactLimit),
                Extract(lines, IndustryLabels, ShortFactLimit),
                Extract(lines, AddressLabels, ShortFactLimit),
                Extract(lines, IntroductionLabels, IntroductionLimit, allowMultipleLines: true));
        }
        catch (ArgumentException)
        {
            // A malformed regular-expression input or unusual source should not make
            // acquisition fail. The original SourceDocument is still retained upstream.
            return TopCvParsedFacts.Empty;
        }
    }

    private static string? Extract(
        IReadOnlyList<string> lines,
        IReadOnlyList<string> labels,
        int maximumLength,
        bool allowMultipleLines = false)
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

                    value = ReadFollowingValue(lines, index + 1, allowMultipleLines);
                }

                value = CleanValue(value, maximumLength);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool TryReadSameLine(string line, string label, out string? value)
    {
        var pattern = $"^{Regex.Escape(label)}(?:\\s*[:\\-–—]\\s*|\\s+)(?<value>.+)$";
        var match = Regex.Match(line.Trim(), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = match.Success ? match.Groups["value"].Value : null;
        return match.Success;
    }

    private static bool IsLabelOnly(string line, string label)
    {
        var normalizedLine = NormalizeLabel(line);
        var normalizedLabel = NormalizeLabel(label);
        return normalizedLine.Equals(normalizedLabel, StringComparison.Ordinal)
            || normalizedLine.Equals($"{normalizedLabel}:", StringComparison.Ordinal)
            || normalizedLine.Equals($"{normalizedLabel} -", StringComparison.Ordinal);
    }

    private static string? ReadFollowingValue(IReadOnlyList<string> lines, int start, bool allowMultipleLines)
    {
        var values = new List<string>();
        for (var index = start; index < lines.Count && values.Count < (allowMultipleLines ? 4 : 1); index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
            {
                if (values.Count > 0)
                {
                    break;
                }

                continue;
            }

            values.Add(line);
            if (!allowMultipleLines)
            {
                break;
            }
        }

        return values.Count == 0 ? null : string.Join(" ", values);
    }

    private static string? CleanValue(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = WhitespaceRegex().Replace(WebUtility.HtmlDecode(value), " ").Trim(' ', ':', '-', '–', '—');
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

    private static string NormalizeLabel(string value) =>
        WhitespaceRegex().Replace(value.Trim().TrimEnd(':', '-', '–', '—'), " ");

    [GeneratedRegex(@"<\s*(?:script|style|noscript|template)\b[^>]*>.*?<\s*/\s*(?:script|style|noscript|template)\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex NoiseTagRegex();

    [GeneratedRegex(@"<\s*/?\s*(?:br|p|div|section|article|li|tr|h[1-6]|header|footer|dt|dd)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
