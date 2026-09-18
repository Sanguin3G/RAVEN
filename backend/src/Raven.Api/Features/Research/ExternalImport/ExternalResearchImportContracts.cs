using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.SavedArtifacts;

namespace Raven.Api.Features.Research.ExternalImport;

/// <summary>
/// Context used to prepare a provider-neutral prompt for an external assistant.
/// The profile and coverage values are hints for the prompt only; they do not
/// turn an external answer into accepted profile truth.
/// </summary>
public sealed record ExternalResearchBriefRequest(
    Company Company,
    CompanyProfileSnapshot? Profile = null,
    EvidenceCoverageResponse? Coverage = null,
    string? ResearchObjective = null,
    IReadOnlyCollection<ResearchTarget>? RequestedTargets = null);

public sealed record ExternalResearchBrief(
    string Objective,
    IReadOnlyList<ResearchTarget> FocusedTargets,
    string Markdown);

/// <summary>Parsed notes returned by an external research assistant.</summary>
public sealed record ExternalResearchImportResult(
    string Summary,
    IReadOnlyList<ResearchClaim> Claims,
    IReadOnlyList<ResearchSourceLead> SourceLeads,
    IReadOnlyList<string> Uncertainties,
    IReadOnlyList<string> SuggestedFollowUps,
    string RawMarkdown)
{
    /// <summary>
    /// Converts the parsed notes into an artifact request. This deliberately
    /// leaves SourceDocumentIds empty: imported URLs remain provider citations
    /// and review context; the External Assist flow does not re-search or
    /// re-crawl them.
    /// </summary>
    public SavedResearchArtifactRequest ToArtifactRequest(
        Guid companyId,
        string title,
        string question,
        DateTimeOffset? completedAt = null,
        Guid? conversationId = null) =>
        new(
            companyId,
            title,
            question,
            Summary,
            SavedResearchType.Deep,
            SourceDocumentIds: [],
            ConversationId: conversationId,
            Origin: SavedResearchOrigin.ExternalImport,
            Objective: question,
            CompletedAt: completedAt,
            SourceLeads: SourceLeads,
            Claims: Claims,
            Uncertainties: Uncertainties,
            RawResponse: RawMarkdown);
}

public interface IExternalResearchBriefGenerator
{
    ExternalResearchBrief Generate(ExternalResearchBriefRequest request);
}

public interface IExternalResearchImportParser
{
    ExternalResearchImportResult Parse(string markdown);
}
