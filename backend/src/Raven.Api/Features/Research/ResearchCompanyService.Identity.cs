using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research.Parsing;
using Raven.Api.Features.Research.Planning;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Identity;
using Raven.Api.Features.Search;
using Raven.Api.Features.Settings;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research.Routing;
using Raven.Api.Features.Research.Coverage;

namespace Raven.Api.Features.Research;

public sealed partial class ResearchCompanyService
{
    public async Task<IReadOnlyList<ResearchIdentityCandidateResponse>?> ListIdentityCandidatesAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.ResearchRuns.AnyAsync(run => run.Id == researchRunId, cancellationToken))
        {
            return null;
        }

        var candidates = await dbContext.Set<ResearchIdentityCandidate>()
            .AsNoTracking()
            .Where(candidate => candidate.ResearchRunId == researchRunId)
            .OrderByDescending(candidate => candidate.Recommended)
            .ThenByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.DisplayName)
            .ToListAsync(cancellationToken);
        return candidates.Select(ToIdentityResponse).ToArray();
    }

    public async Task<ResearchRunResponse?> SelectIdentityAsync(
        Guid researchRunId,
        SelectResearchIdentityRequest request,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ResearchRuns
            .SingleOrDefaultAsync(candidate => candidate.Id == researchRunId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        if (request is null)
        {
            throw new BadHttpRequestException("Select a research identity candidate.");
        }

        if (run.Stage != ResearchStage.AwaitingIdentitySelection)
        {
            throw new BadHttpRequestException("This research run is not waiting for an identity selection.");
        }

        var identityCandidates = await dbContext.Set<ResearchIdentityCandidate>()
            .Where(candidate => candidate.ResearchRunId == researchRunId)
            .ToListAsync(cancellationToken);
        var selected = identityCandidates.SingleOrDefault(candidate => candidate.Id == request.CandidateId);
        if (selected is null)
        {
            throw new BadHttpRequestException("The selected identity candidate does not belong to this research run.");
        }

        foreach (var candidate in identityCandidates)
        {
            candidate.Selected = candidate.Id == selected.Id;
        }

        var company = await dbContext.Companies
            .SingleAsync(candidate => candidate.Id == run.CompanyId, cancellationToken);
        dbContext.ResearchCandidates.RemoveRange(
            dbContext.ResearchCandidates.Where(candidate => candidate.ResearchRunId == run.Id));
        await dbContext.SaveChangesAsync(cancellationToken);

        run.ResolvedIdentityCandidateId = selected.Id;
        run.Error = null;
        run.CompletedAt = null;
        var resolvedIdentity = ToResolvedEntity(selected);
        run.Stage = ResearchStage.Discovering;
        run.Status = ResearchRunStatus.Searching;
        var targetIdentity = new ResearchIdentityInput(
            resolvedIdentity.DisplayName,
            resolvedIdentity.LegalName ?? company.LegalName,
            resolvedIdentity.Website ?? BuildWebsite(resolvedIdentity.OfficialDomain) ?? company.Website,
            resolvedIdentity.Country ?? company.Country,
            company.RegistrationNumber,
            company.Headquarters,
            run.ResearchHint);

        try
        {
            var (searchResults, errors) = await sourceDiscovery.SearchAsync(run, targetIdentity, cancellationToken);
            if (searchResults.Count == 0)
            {
                return await FailAsync(
                    run,
                    BuildFailureMessage("Search completed but returned no useful source URLs.", errors),
                    cancellationToken);
            }

            var targetCompany = new Company
            {
                Name = resolvedIdentity.DisplayName,
                LegalName = resolvedIdentity.LegalName,
                Website = targetIdentity.Website,
                Country = targetIdentity.Country,
                RegistrationNumber = targetIdentity.RegistrationNumber,
                Headquarters = targetIdentity.Headquarters
            };
            var officialWebsite = ResearchDiscoveryCoordinator.ResolveOfficialWebsite(targetCompany, searchResults);
            var rankedCandidates = sourceDiscovery.RankCandidates(targetCompany, searchResults, officialWebsite, GetTargets(run));
            if (rankedCandidates.Length == 0)
            {
                return await FailAsync(
                    run,
                    BuildFailureMessage("Search completed but returned no useful source URLs.", errors),
                    cancellationToken);
            }

            var drafts = rankedCandidates
                .Select((ranked, index) => new CandidateDraft(
                    Guid.NewGuid(),
                    ranked,
                    index < Math.Min(MaximumRecommendedCandidates, rankedCandidates.Length),
                    index < Math.Min(MaximumRecommendedCandidates, rankedCandidates.Length)))
                .ToArray();
            var settings = await ReadSettingsAsync(cancellationToken);
            if (settings?.AiSourceRerankingEnabled == true && sourceSemanticReranker is not null)
            {
                drafts = await sourceDiscovery.ApplySemanticRerankingAsync(
                    run,
                    resolvedIdentity,
                    drafts,
                    settings.GroundingModel,
                    cancellationToken);
            }

            drafts = sourceDiscovery.ApplyCoverageAwareSelection(drafts, GetTargets(run));

            run.UniqueCandidates = drafts.Length;
            run.RecommendedCandidates = drafts.Count(candidate => candidate.Recommended);
            run.Stage = ResearchStage.AwaitingSourceSelection;
            run.Status = ResearchRunStatus.Searching;
            run.Error = errors.Count == 0 ? null : BuildFailureMessage("Some search queries failed.", errors);
            sourceDiscovery.AddCandidateEntities(drafts, run.Id, null);
            await dbContext.SaveChangesAsync(cancellationToken);
            await WriteEventAsync(run, ResearchEventCategory.IdentitySelected, ResearchEventStatus.Completed,
                null, $"Research target selected: {resolvedIdentity.DisplayName}.", cancellationToken);
            await WriteEventAsync(run, ResearchEventCategory.CandidateDiscovery, ResearchEventStatus.WaitingForUser,
                searchProvider.Id, $"Rebuilt target-specific discovery: {run.UniqueCandidates} unique candidates; {run.RecommendedCandidates} recommended.", cancellationToken);
            return ToResponse(run);
        }
        catch (ProviderException exception)
        {
            return await FailAsync(run, exception.Message, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return await FailAsync(run, "Research provider could not be reached.", cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await FailAsync(run, "Research provider timed out.", cancellationToken);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Identity-selected research discovery failed for run {ResearchRunId}", run.Id);
            return await FailAsync(run, "Research failed unexpectedly. Please retry.", cancellationToken);
        }
    }

    private static ResolvedResearchEntity ToResolvedEntity(ResearchIdentityCandidate candidate) => new(
        candidate.TemporaryId,
        candidate.DisplayName,
        candidate.LegalName,
        candidate.Country,
        candidate.Website,
        candidate.OfficialDomain,
        candidate.EntityType,
        candidate.RelationshipHint,
        candidate.Confidence,
        candidate.Rationale,
        DeserializeGuidArray(candidate.SupportingCandidateIdsJson),
        candidate.Recommended);

    private static ResearchIdentityCandidateResponse ToIdentityResponse(ResearchIdentityCandidate candidate) => new(
        candidate.Id,
        candidate.ResearchRunId,
        candidate.TemporaryId,
        candidate.DisplayName,
        candidate.LegalName,
        candidate.Country,
        candidate.Website,
        candidate.OfficialDomain,
        candidate.EntityType,
        candidate.RelationshipHint,
        candidate.Confidence,
        candidate.Rationale,
        DeserializeGuidArray(candidate.SupportingCandidateIdsJson),
        candidate.Recommended,
        candidate.Selected,
        candidate.CreatedAt);

    private static string? BuildWebsite(string? domain) =>
        string.IsNullOrWhiteSpace(domain) ? null : $"https://{domain.Trim()}";

    private static bool IsRecommended(SourceSemanticAssessment assessment) =>
        assessment.Recommended &&
        assessment.EntityRelationship is not EntityRelationship.DifferentEntity &&
        assessment.Relevance is not CandidateRelevance.Low;

    private async Task<ResearchIdentityInput> BuildInitialIdentityAsync(
        Company company,
        string? researchHint,
        bool useAcceptedProfileIdentity,
        ResolvedIdentitySnapshot? resolvedIdentity,
        CancellationToken cancellationToken)
    {
        if (resolvedIdentity is not null)
        {
            return new ResearchIdentityInput(
                resolvedIdentity.DisplayName,
                resolvedIdentity.LegalNameHint,
                string.IsNullOrWhiteSpace(resolvedIdentity.OfficialDomainHint) ? company.Website : $"https://{resolvedIdentity.OfficialDomainHint}",
                resolvedIdentity.Country ?? company.Country,
                company.RegistrationNumber,
                resolvedIdentity.Region ?? company.Headquarters,
                researchHint);
        }
        if (!useAcceptedProfileIdentity || profilePersistence is null)
        {
            return ApplyExplicitIdentityHints(ResearchIdentityInput.FromCompany(company, researchHint));
        }

        try
        {
            var profile = await profilePersistence.GetCurrentProfileAsync(company.Id, cancellationToken);
            if (profile is null)
            {
                return ApplyExplicitIdentityHints(ResearchIdentityInput.FromCompany(company, researchHint));
            }

            return new ResearchIdentityInput(
                profile.DisplayName?.Trim() is { Length: > 0 } displayName ? displayName : company.Name,
                profile.LegalName?.Trim() is { Length: > 0 } legalName ? legalName : company.LegalName,
                profile.Website?.Trim() is { Length: > 0 } website ? website : company.Website,
                profile.Country?.Trim() is { Length: > 0 } country ? country : company.Country,
                profile.RegistrationNumberOrTaxId?.Trim() is { Length: > 0 } registration ? registration : company.RegistrationNumber,
                profile.Headquarters?.Trim() is { Length: > 0 } headquarters ? headquarters : company.Headquarters,
                researchHint) with
            {
                Country = ReadExplicitHint(researchHint, "Country") ??
                          (profile.Country?.Trim() is { Length: > 0 } profileCountry ? profileCountry : company.Country),
                LegalName = ReadExplicitHint(researchHint, "Legal name") ??
                            (profile.LegalName?.Trim() is { Length: > 0 } profileLegalName ? profileLegalName : company.LegalName)
            };
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Refresh remains available with stable Company identity if old profile data cannot be read.
            return ApplyExplicitIdentityHints(ResearchIdentityInput.FromCompany(company, researchHint));
        }
    }

    private static ResearchIdentityInput ApplyExplicitIdentityHints(ResearchIdentityInput identity) => identity with
    {
        Country = ReadExplicitHint(identity.ResearchHint, "Country") ?? identity.Country,
        LegalName = ReadExplicitHint(identity.ResearchHint, "Legal name") ?? identity.LegalName
    };

    private static string? ReadExplicitHint(string? researchHint, string label)
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
