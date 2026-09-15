using System.Text;

namespace Raven.Api.Features.Chat;

public static class ChatText
{
    public static string NormalizeQuestion(string? value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsControl(character)) continue;
            builder.Append(char.IsWhiteSpace(character) ? ' ' : character);
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    public static string Bound(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, maximum)];
    }
}
