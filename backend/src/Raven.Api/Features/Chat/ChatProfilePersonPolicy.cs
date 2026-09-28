using System.Text.RegularExpressions;
using Raven.Api.Features.Profiles;

namespace Raven.Api.Features.Chat;

internal sealed record ChatProfilePersonMatch(ProfileLeader Leader, IReadOnlyList<Guid> SourceIds);

internal static partial class ChatProfilePersonPolicy
{
    public static ChatProfilePersonMatch? Match(CompanyProfileVersion profile, string question)
    {
        var normalized = ChatText.NormalizeQuestion(question).Trim(' ', '?', '.', '!');
        var matches = profile.Leadership.Where(leader => IsIdentityQuestion(normalized, leader.Name)).ToArray();
        if (matches.Length != 1) return null;

        var sources = profile.Evidence
            .Where(evidence => evidence.FieldPath.Equals("leadership", StringComparison.OrdinalIgnoreCase) ||
                evidence.FieldPath.StartsWith("leadership[", StringComparison.OrdinalIgnoreCase))
            .SelectMany(evidence => evidence.SourceDocumentIds).Distinct().Take(2).ToArray();
        return sources.Length == 0 ? null : new ChatProfilePersonMatch(matches[0], sources);
    }

    public static bool PreferVietnamese(string question, IReadOnlyList<ChatMessage> recentMessages) =>
        VietnameseWords().IsMatch(question) || recentMessages.TakeLast(6)
            .Any(message => message.Role == ChatMessageRole.User && VietnameseWords().IsMatch(message.Content));

    private static bool IsIdentityQuestion(string question, string name)
    {
        var escaped = Regex.Escape(name.Trim());
        return Regex.IsMatch(question, $"^(?:{escaped}|who is {escaped}|{escaped} là ai|{escaped} là ai vậy|trong profile có thông tin (?:của |về )?{escaped} không|profile có thông tin (?:của |về )?{escaped} không)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"[ăâđêôơưáàảãạấầẩẫậắằẳẵặéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ]|\b(?:có|các|công ty|nào|của|từ|đến)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VietnameseWords();
}
