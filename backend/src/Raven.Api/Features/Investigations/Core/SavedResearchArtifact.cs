using System.ComponentModel.DataAnnotations.Schema;
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
    /// Original user-supplied research material, when available. It remains
    /// untrusted material and is never used as accepted profile evidence.
    /// </summary>
    public string? RawResponse { get; init; }

    /// <summary>
    /// Read-only vocabulary alias for clients that call the saved answer a
    /// result. The persisted field is <see cref="Summary"/>.
    /// </summary>
    [JsonIgnore]
    public string Result => Summary;

    public DateTimeOffset CreatedAt { get; init; }
    public SavedResearchType ResearchType { get; init; }
    /// <summary>
    /// Describes where the research material came from. This is provenance,
    /// not a trust ranking: managed and imported material is still reviewable
    /// research material and is never an accepted profile fact by itself.
    /// </summary>
    public SavedResearchOrigin Origin { get; init; } = SavedResearchOrigin.RavenNative;
    public string? Model { get; init; }
    public string? Provider { get; init; }
    public string? Objective { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? ManagedResearchJobId { get; init; }

    /// <summary>Bounded JSON persistence for provenance metadata.</summary>
    [JsonIgnore]
    public string ProviderMetadataJson { get; internal set; } = "{}";

    /// <summary>Bounded JSON persistence for unverified URL leads.</summary>
    [JsonIgnore]
    public string SourceLeadsJson { get; internal set; } = "[]";

    /// <summary>Bounded JSON persistence for reviewable claims.</summary>
    [JsonIgnore]
    public string ClaimsJson { get; internal set; } = "[]";

    /// <summary>Bounded JSON persistence for provider/import caveats.</summary>
    [JsonIgnore]
    public string UncertaintiesJson { get; internal set; } = "[]";

    /// <summary>
    /// Optional provider metadata that is useful for diagnostics (for example a
    /// remote request ID or usage summary). It is intentionally not mapped by
    /// the current EF adapter; persistence wiring can choose a bounded JSON
    /// representation without changing this domain contract.
    /// </summary>
    [NotMapped]
    public IDictionary<string, string> ProviderMetadata { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// URLs and descriptive details supplied by a managed provider or imported
    /// assistant answer. These are source leads, not SourceDocument evidence.
    /// </summary>
    [NotMapped]
    public ICollection<ResearchSourceLead> SourceLeads { get; } = new List<ResearchSourceLead>();

    /// <summary>Reviewable claims linked to source leads, never profile evidence.</summary>
    [NotMapped]
    public ICollection<ResearchClaim> Claims { get; } = new List<ResearchClaim>();

    /// <summary>Unresolved caveats reported by the researcher/provider.</summary>
    [NotMapped]
    public ICollection<string> Uncertainties { get; } = new List<string>();

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
            RawResponse = RawResponse,
            CreatedAt = CreatedAt,
            ResearchType = ResearchType,
            Origin = Origin,
            Model = Model,
            Provider = Provider,
            Objective = Objective,
            CompletedAt = CompletedAt,
            ManagedResearchJobId = ManagedResearchJobId,
            ProviderMetadataJson = ProviderMetadataJson,
            SourceLeadsJson = SourceLeadsJson,
            ClaimsJson = ClaimsJson,
            UncertaintiesJson = UncertaintiesJson,
            SourceCount = SourceCount,
            SourceDocumentIdsJson = SourceDocumentIdsJson
        };

        foreach (var sourceDocumentId in SourceDocumentIds)
        {
            clone.SourceDocumentIds.Add(sourceDocumentId);
        }

        foreach (var sourceLead in SourceLeads)
        {
            clone.SourceLeads.Add(sourceLead with { });
        }

        foreach (var claim in Claims)
        {
            clone.Claims.Add(claim with
            {
                SupportingSourceLeadIds = claim.SupportingSourceLeadIds?.ToArray()
            });
        }

        foreach (var uncertainty in Uncertainties)
        {
            clone.Uncertainties.Add(uncertainty);
        }

        foreach (var metadata in ProviderMetadata)
        {
            clone.ProviderMetadata[metadata.Key] = metadata.Value;
        }

        return clone;
    }
}

public enum SavedResearchType
{
    Fast,
    Deep
}

/// <summary>Provenance of saved research material, independent of provider.</summary>
public enum SavedResearchOrigin
{
    RavenNative,
    ManagedAi,
    ExternalImport
}

/// <summary>
/// A URL lead supplied by research material. A lead is retained as provider
/// provenance and review context; External Assist does not turn it into a new
/// crawl or SourceDocument automatically.
/// </summary>
public sealed record ResearchSourceLead(
    string Url,
    string? Title = null,
    string? Publisher = null,
    DateTimeOffset? PublishedAt = null,
    string? SourceType = null,
    string? Supports = null)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>
/// A factual statement from a managed or imported result. Source lead IDs are
/// provenance links only; they are deliberately distinct from profile evidence
/// source-document IDs.
/// </summary>
public sealed record ResearchClaim(
    string Field,
    string Statement,
    IReadOnlyList<Guid>? SupportingSourceLeadIds = null,
    string? Confidence = null,
    string? Notes = null);
