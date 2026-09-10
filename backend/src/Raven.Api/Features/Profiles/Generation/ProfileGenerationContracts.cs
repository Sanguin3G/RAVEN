using Raven.Api.Features.Research;

namespace Raven.Api.Features.Profiles.Generation;

/// <summary>
/// User-entered identity values are hints for research. They are intentionally
/// kept separate from the generated profile facts.
/// </summary>
public sealed record ProfileIdentityHints(
    string? DisplayName = null,
    string? LegalName = null,
    string? Website = null,
    string? Country = null,
    string? Headquarters = null,
    string? RegistrationNumberOrTaxId = null,
    string? ResearchHint = null);

/// <summary>
/// Explicit input for one profile-generation operation. The caller supplies the
/// already acquired documents and the server-owned validation context; this
/// service never queries a database or accepts arbitrary source URLs.
/// </summary>
public sealed record ProfileGenerationInput(
    Guid CompanyId,
    Guid ResearchRunId,
    ProfileIdentityHints IdentityHints,
    IReadOnlyCollection<SourceDocument> SourceDocuments,
    ProfileValidationContext ValidationContext);

public sealed class ProfileGenerationOptions
{
    public const string SectionName = "Research:ProfileGeneration";

    public string Model { get; set; } = "gemini-3.5-flash-lite";
    public string PromptTemplateVersion { get; set; } = "company-profile-v1";
}

public sealed record ProfileEvidencePackage(
    Raven.Api.Features.Ai.AiEvidencePayload Payload,
    IReadOnlyList<ProfileInputSource> Sources,
    int CharacterCount);

/// <summary>
/// The authority rank is diagnostic metadata for deterministic source ordering;
/// it is not a confidence score and is not presented as a factual claim.
/// </summary>
public sealed record ProfileInputSource(
    Guid SourceDocumentId,
    Raven.Api.Features.Ai.AiEvidenceItem Evidence,
    int AuthorityRank);

public interface IProfileInputBuilder
{
    ProfileEvidencePackage Build(
        ProfileIdentityHints identityHints,
        IReadOnlyCollection<SourceDocument> sourceDocuments);
}

public interface IProfileGenerationService
{
    Task<ProfileGenerationResult> GenerateAsync(
        ProfileGenerationInput input,
        CancellationToken cancellationToken = default);
}

public sealed record ProfileGenerationResult(
    CompanyProfileCandidate? Candidate,
    IReadOnlyList<string> Warnings,
    string Provider,
    string Model,
    string PromptTemplateVersion,
    TimeSpan Duration,
    Raven.Api.Features.Ai.AiUsage? Usage = null,
    string? ExternalRequestId = null,
    Raven.Api.Features.Ai.AiFailure? Failure = null,
    bool IsValid = false)
{
    public bool Succeeded => Candidate is not null && Failure is null && IsValid;
}
