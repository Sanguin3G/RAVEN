namespace Raven.Api.Features.Research.Identity;

/// <summary>
/// The model describes the shape of the query, not the workflow decision.
/// The application maps this topology to Resolved/Ambiguous/NeedsMoreInfo/
/// Unknown after deterministic validation.
/// </summary>
public enum IdentityQueryInterpretation
{
    SpecificEntity,
    CorporateFamilyShorthand,
    NameCollision,
    Unknown
}

public enum IdentityResolutionStatus { Resolved, Ambiguous, NeedsMoreInfo, Unknown }
public enum IdentityAmbiguityType { None, CorporateFamily, NameCollision, Unclear }
public enum IdentityResolutionMethod { ExplicitIdentifier, ModelKnowledge, UserConfirmedExactInput, AcceptedExistingIdentity }

public enum IdentityHintKind
{
    Country,
    Website,
    LegalName,
    RegistrationNumber,
    Headquarters,
    Region
}

public enum IdentityEntityType
{
    ParentGroup,
    Company,
    Subsidiary,
    Affiliate,
    Brand,
    Unknown
}

public enum IdentityRelationshipToQuery
{
    Exact,
    Alias,
    Parent,
    Subsidiary,
    SimilarName,
    Possible
}

public enum IdentityConfidence
{
    Low,
    Medium,
    High
}

public sealed record IdentityResolutionRequest(
    string Name,
    string? LegalName = null,
    string? Website = null,
    string? Country = null,
    string? RegistrationNumber = null,
    string? Headquarters = null,
    string? ResearchHint = null,
    bool ConfirmExactName = false,
    bool AllowModelKnowledge = true);

/// <summary>
/// An identity/search hint from model knowledge. It is never an accepted
/// Company Profile fact and is intentionally bounded by the resolver.
/// </summary>
public sealed record IdentityOption(
    string TemporaryId,
    string DisplayName,
    string? LegalName,
    string? Country,
    string? Region,
    string? OfficialDomain,
    IdentityEntityType EntityType,
    string? ParentTemporaryId,
    IdentityRelationshipToQuery RelationshipToQuery,
    IdentityConfidence Confidence,
    string? ShortDescription);

public sealed record IdentityTopologyResponse(
    IdentityQueryInterpretation Interpretation,
    IReadOnlyList<IdentityOption> Entities,
    IReadOnlyList<IdentityHintKind> RequestedHints,
    string? Message = null,
    string? Warning = null,
    string? ModelUsed = null,
    string? ResolutionMethod = null,
    AiFailureInfo? Failure = null);

public sealed record IdentityResolutionResponse(
    IdentityResolutionStatus Status,
    IdentityAmbiguityType AmbiguityType,
    string? RecommendedEntityId,
    IReadOnlyList<IdentityOption> Entities,
    IReadOnlyList<IdentityHintKind> RequestedHints,
    string? Message,
    IdentityResolutionMethod ResolutionMethod,
    string? ModelUsed = null,
    string? Warning = null);

/// <summary>
/// Sanitized provider failure information. The topology resolver does not
/// expose raw provider response bodies or hidden model reasoning.
/// </summary>
public sealed record AiFailureInfo(
    string Code,
    string Message,
    bool Retryable,
    int? HttpStatus = null);

public interface IIdentityKnowledgeResolver
{
    Task<IdentityTopologyResponse> ResolveAsync(
        IdentityResolutionRequest request,
        CancellationToken cancellationToken = default);
}
