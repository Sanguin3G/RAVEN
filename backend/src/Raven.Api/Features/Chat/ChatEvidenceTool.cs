using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Chat;

public sealed class ChatEvidenceTool(RavenDbContext dbContext)
{
    public async Task<ChatEvidenceToolResult> ReadExcerptAsync(
        ChatAgentRequest request,
        Guid sourceDocumentId,
        CancellationToken cancellationToken)
    {
        var allowed = request.Profile.Evidence
            .SelectMany(item => item.SourceDocumentIds)
            .ToHashSet();
        if (!allowed.Contains(sourceDocumentId))
        {
            return ChatEvidenceToolResult.Failure("source_not_in_profile", "The requested source is not linked to the accepted profile.");
        }

        var source = await dbContext.SourceDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == sourceDocumentId && item.CompanyId == request.CompanyId, cancellationToken);
        if (source is null)
        {
            return ChatEvidenceToolResult.Failure("source_not_found", "The requested source is no longer available for this company.");
        }

        var fieldPaths = request.Profile.Evidence
            .Where(item => item.SourceDocumentIds.Contains(sourceDocumentId))
            .Select(item => item.FieldPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return ChatEvidenceToolResult.Success(
            source.Id,
            source.Title,
            source.Url,
            string.Join(", ", fieldPaths),
            ChatText.Bound(source.Content, 8_000));
    }
}

public sealed record ChatEvidenceToolResult(
    bool Succeeded,
    Guid? SourceDocumentId,
    string? Title,
    string? Url,
    string? FieldPaths,
    string? Content,
    string? ErrorCode,
    string? Error)
{
    public static ChatEvidenceToolResult Success(Guid id, string? title, string url, string fieldPaths, string content) =>
        new(true, id, title, url, fieldPaths, content, null, null);

    public static ChatEvidenceToolResult Failure(string code, string error) =>
        new(false, null, null, null, null, null, code, error);

    public string ToPromptText() => Succeeded
        ? $"SOURCE_ID: {SourceDocumentId}\nTITLE: {Title}\nURL: {Url}\nPROFILE_FIELDS: {FieldPaths}\nCONTENT:\n{Content}"
        : $"TOOL_ERROR: {ErrorCode}\n{Error}";
}
