using System.Text.Json.Serialization;

namespace Raven.Api.Features.Research.SavedArtifacts;

/// <summary>
/// A saved answer to a research question. This is a research output bookmark,
/// not an accepted <c>CompanyProfileVersion</c> and must never replace one.
/// </summary>
public sealed class SavedResearchArtifact
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }

    // These are deliberately GUID hooks only. The corresponding Conversation
    // and DeepResearchRun entities are future work and are not required to save
    // an artifact today.
    public Guid? ConversationId { get; init; }
    public Guid? DeepResearchRunId { get; init; }

    public required string Title { get; init; }
    public required string Question { get; init; }
    public required string Summary { get; init; }

    /// <summary>
    /// Read-only vocabulary alias for clients that call the saved answer a
    /// result. The persisted field is <see cref="Summary"/>.
    /// </summary>
    [JsonIgnore]
    public string Result => Summary;

    public DateTimeOffset CreatedAt { get; init; }
    public SavedResearchType ResearchType { get; init; }
    public string? Model { get; init; }

    /// <summary>
    /// Server-derived count. Callers provide source IDs; they never provide this
    /// value independently.
    /// </summary>
    public int SourceCount { get; internal set; }

    /// <summary>Server-validated references to SourceDocument rows.</summary>
    public ICollection<Guid> SourceDocumentIds { get; } = new List<Guid>();

    /// <summary>
    /// Persistence representation for the source-ID collection. An EF adapter
    /// may instead map the IDs through a normalized join table.
    /// </summary>
    [JsonIgnore]
    public string SourceDocumentIdsJson { get; internal set; } = "[]";

    internal SavedResearchArtifact Clone()
    {
        var clone = new SavedResearchArtifact
        {
            Id = Id,
            CompanyId = CompanyId,
            ConversationId = ConversationId,
            DeepResearchRunId = DeepResearchRunId,
            Title = Title,
            Question = Question,
            Summary = Summary,
            CreatedAt = CreatedAt,
            ResearchType = ResearchType,
            Model = Model,
            SourceCount = SourceCount,
            SourceDocumentIdsJson = SourceDocumentIdsJson
        };

        foreach (var sourceDocumentId in SourceDocumentIds)
        {
            clone.SourceDocumentIds.Add(sourceDocumentId);
        }

        return clone;
    }
}

public enum SavedResearchType
{
    Fast,
    Deep
}
