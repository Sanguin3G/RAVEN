using Raven.Api.Features.Companies;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Sources;
using Day6IdentitySnapshot = Raven.Api.Features.Research.Identity.ResolvedIdentitySnapshot;

namespace Raven.Api.Features.Research;

/// <summary>Identity and semantic-candidate helpers used by the research coordinator.</summary>
public sealed partial class ResearchCompanyService
{
    private static ResolvedResearchEntity IdentityToResolvedEntity(ResearchIdentityCandidate candidate) => new(
        candidate.TemporaryId, candidate.DisplayName, candidate.LegalName, candidate.Country, candidate.Website,
        candidate.OfficialDomain, candidate.EntityType, candidate.RelationshipHint, candidate.Confidence,
        candidate.Rationale, DeserializeGuidArray(candidate.SupportingCandidateIdsJson), candidate.Recommended);

    private static ResearchIdentityCandidateResponse IdentityToResponse(ResearchIdentityCandidate candidate) => new(
        candidate.Id, candidate.ResearchRunId, candidate.TemporaryId, candidate.DisplayName, candidate.LegalName,
        candidate.Country, candidate.Website, candidate.OfficialDomain, candidate.EntityType, candidate.RelationshipHint,
        candidate.Confidence, candidate.Rationale, DeserializeGuidArray(candidate.SupportingCandidateIdsJson),
        candidate.Recommended, candidate.Selected, candidate.CreatedAt);

    private static string? IdentityBuildWebsite(string? domain) =>
        string.IsNullOrWhiteSpace(domain) ? null : $"https://{domain.Trim()}";

    private static bool IdentityIsRecommended(SourceSemanticAssessment assessment) =>
        assessment.Recommended && assessment.EntityRelationship is not EntityRelationship.DifferentEntity && assessment.Relevance is not CandidateRelevance.Low;

    private static SourceSemanticCandidate IdentityToSemanticCandidate(CandidateDraft draft) => new(
        draft.Id, draft.Ranked.Source.Url, draft.Ranked.Classification.Domain ?? draft.Ranked.Source.SourceDomain,
        draft.Ranked.Source.Title, draft.Ranked.Source.Snippet, draft.Ranked.Classification.SourceKind,
        draft.Ranked.Score, draft.Ranked.Reasons);

    private async Task<ResearchIdentityInput> IdentityBuildInitialAsync(Company company, string? researchHint,
        bool useAcceptedProfileIdentity, Day6IdentitySnapshot? resolvedIdentity, CancellationToken cancellationToken)
    {
        if (resolvedIdentity is not null)
        {
            return new ResearchIdentityInput(resolvedIdentity.DisplayName, resolvedIdentity.LegalNameHint,
                string.IsNullOrWhiteSpace(resolvedIdentity.OfficialDomainHint) ? company.Website : $"https://{resolvedIdentity.OfficialDomainHint}",
                resolvedIdentity.Country ?? company.Country, company.RegistrationNumber, resolvedIdentity.Region ?? company.Headquarters, researchHint);
        }
        if (!useAcceptedProfileIdentity || profilePersistence is null) return IdentityApplyExplicitHints(ResearchIdentityInput.FromCompany(company, researchHint));
        try
        {
            var profile = await profilePersistence.GetCurrentProfileAsync(company.Id, cancellationToken);
            if (profile is null) return IdentityApplyExplicitHints(ResearchIdentityInput.FromCompany(company, researchHint));
            return new ResearchIdentityInput(
                profile.DisplayName?.Trim() is { Length: > 0 } displayName ? displayName : company.Name,
                profile.LegalName?.Trim() is { Length: > 0 } legalName ? legalName : company.LegalName,
                profile.Website?.Trim() is { Length: > 0 } website ? website : company.Website,
                profile.Country?.Trim() is { Length: > 0 } country ? country : company.Country,
                profile.RegistrationNumberOrTaxId?.Trim() is { Length: > 0 } registration ? registration : company.RegistrationNumber,
                profile.Headquarters?.Trim() is { Length: > 0 } headquarters ? headquarters : company.Headquarters, researchHint) with
            {
                Country = IdentityReadExplicitHint(researchHint, "Country") ?? (profile.Country?.Trim() is { Length: > 0 } profileCountry ? profileCountry : company.Country),
                LegalName = IdentityReadExplicitHint(researchHint, "Legal name") ?? (profile.LegalName?.Trim() is { Length: > 0 } profileLegalName ? profileLegalName : company.LegalName)
            };
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return IdentityApplyExplicitHints(ResearchIdentityInput.FromCompany(company, researchHint));
        }
    }

    private static ResearchIdentityInput IdentityApplyExplicitHints(ResearchIdentityInput identity) => identity with
    {
        Country = IdentityReadExplicitHint(identity.ResearchHint, "Country") ?? identity.Country,
        LegalName = IdentityReadExplicitHint(identity.ResearchHint, "Legal name") ?? identity.LegalName
    };

    private static string? IdentityReadExplicitHint(string? researchHint, string label)
    {
        if (string.IsNullOrWhiteSpace(researchHint)) return null;
        var marker = $"{label}:";
        var start = researchHint.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += marker.Length;
        var end = researchHint.IndexOf(';', start);
        var value = (end >= 0 ? researchHint[start..end] : researchHint[start..]).Trim();
        return value.Length == 0 ? null : value;
    }
}
