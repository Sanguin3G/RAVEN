using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.DeepResearch;
using Raven.Api.Features.Monitoring;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Changes;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Organization;
using Raven.Api.Features.Research.SavedArtifacts;
using Raven.Api.Features.Research.Briefings;

namespace Raven.Api.Features.Companies;

/// <summary>
/// Reconciles a canonical and duplicate Company inside one database transaction.
/// It owns relationship migration, provenance rewriting, profile-history ordering,
/// and duplicate-source reuse; the lifecycle facade only exposes the user action.
/// </summary>
public sealed partial class CompanyMergeService(RavenDbContext dbContext)
{
    public async Task<CompanyMergePreviewResponse?> PreviewAsync(
        CompanyMergePreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CanonicalCompanyId == Guid.Empty ||
            request.DuplicateCompanyId == Guid.Empty ||
            request.CanonicalCompanyId == request.DuplicateCompanyId)
        {
            return null;
        }

        var companies = await dbContext.Companies
            .AsNoTracking()
            .Where(item => item.Id == request.CanonicalCompanyId || item.Id == request.DuplicateCompanyId)
            .ToListAsync(cancellationToken);
        var canonical = companies.SingleOrDefault(item => item.Id == request.CanonicalCompanyId);
        var duplicate = companies.SingleOrDefault(item => item.Id == request.DuplicateCompanyId);
        return canonical is null || duplicate is null
            ? null
            : await BuildPreviewAsync(canonical, duplicate, cancellationToken);
    }

    public async Task<CompanyMergeResult> ConfirmAsync(
        CompanyMergeConfirmRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CanonicalCompanyId == Guid.Empty ||
            request.DuplicateCompanyId == Guid.Empty ||
            request.CanonicalCompanyId == request.DuplicateCompanyId)
        {
            return new CompanyMergeResult(
                CompanyMergeOutcome.Invalid,
                Error: "Canonical and duplicate companies must be different valid IDs.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var companies = await dbContext.Companies
                .AsNoTracking()
                .Where(item => item.Id == request.CanonicalCompanyId || item.Id == request.DuplicateCompanyId)
                .ToListAsync(cancellationToken);
            var canonical = companies.SingleOrDefault(item => item.Id == request.CanonicalCompanyId);
            var duplicate = companies.SingleOrDefault(item => item.Id == request.DuplicateCompanyId);
            if (canonical is null || duplicate is null)
            {
                return new CompanyMergeResult(CompanyMergeOutcome.NotFound);
            }

            var preview = await BuildPreviewAsync(canonical, duplicate, cancellationToken);
            if (!request.Confirm)
            {
                return new CompanyMergeResult(
                    CompanyMergeOutcome.ConfirmationRequired,
                    Preview: preview,
                    Error: "The merge must be explicitly confirmed.");
            }

            var duplicateSources = await dbContext.SourceDocuments
                .AsNoTracking()
                .Where(source => source.CompanyId == duplicate.Id)
                .ToListAsync(cancellationToken);
            var canonicalSources = await dbContext.SourceDocuments
                .AsNoTracking()
                .Where(source => source.CompanyId == canonical.Id)
                .ToListAsync(cancellationToken);
            var sourceMap = BuildSourceMap(duplicateSources, canonicalSources);

            var duplicateProfileCandidateIds = await dbContext.CompanyProfileCandidates
                .AsNoTracking()
                .Where(profile => profile.CompanyId == duplicate.Id)
                .Select(profile => profile.Id)
                .ToArrayAsync(cancellationToken);

            await RewriteSourceReferencesAsync(sourceMap, cancellationToken);
            await ReconcileMonitoringAsync(canonical.Id, duplicate.Id, cancellationToken);
            var profileVersionMerge = await PrepareProfileVersionMergeAsync(canonical.Id, duplicate.Id, cancellationToken);

            await ReassignCompanyAsync(dbContext.ResearchRuns, run => run.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.SourceDocuments, source => source.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.CompanyProfileCandidates, profile => profile.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.CompanyProfileVersions, profile => profile.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.ProfileChanges, change => change.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.DeepResearchRuns, run => run.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.SavedResearchArtifacts, artifact => artifact.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.ManagedResearchJobs, job => job.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.ManagedResearchInvestigations, item => item.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.ResearchContextAttachments, item => item.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.InvestigationReviewStates, state => state.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.ResearchBriefings, brief => brief.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.BriefingGenerationJobs, job => job.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.InvestigationOrganizationRevisions, revision => revision.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.ExternalResearchAnalysisJobs, job => job.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await RewriteMovedProfilePayloadsAsync(
                canonical.Id,
                duplicateProfileCandidateIds,
                profileVersionMerge,
                cancellationToken);

            var sourceIdsToDelete = sourceMap.Keys.ToArray();
            if (sourceIdsToDelete.Length > 0)
            {
                await dbContext.SourceDocuments
                    .Where(source => source.Id != Guid.Empty && sourceIdsToDelete.Contains(source.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            var mergedAt = DateTimeOffset.UtcNow;
            var mergedWebsite = PreferCanonicalValue(canonical.Website, duplicate.Website);
            var mergedCountry = PreferCanonicalValue(canonical.Country, duplicate.Country);
            var mergedLegalName = PreferCanonicalValue(canonical.LegalName, duplicate.LegalName);
            var mergedRegistrationNumber = PreferCanonicalValue(canonical.RegistrationNumber, duplicate.RegistrationNumber);
            var mergedHeadquarters = PreferCanonicalValue(canonical.Headquarters, duplicate.Headquarters);
            var mergedLastResearchedAt = canonical.LastResearchedAt is null
                ? duplicate.LastResearchedAt
                : duplicate.LastResearchedAt is null || canonical.LastResearchedAt >= duplicate.LastResearchedAt
                    ? canonical.LastResearchedAt
                    : duplicate.LastResearchedAt;
            await dbContext.Companies
                .Where(company => company.Id == canonical.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(company => company.Website, mergedWebsite)
                    .SetProperty(company => company.Country, mergedCountry)
                    .SetProperty(company => company.LegalName, mergedLegalName)
                    .SetProperty(company => company.RegistrationNumber, mergedRegistrationNumber)
                    .SetProperty(company => company.Headquarters, mergedHeadquarters)
                    .SetProperty(company => company.LastResearchedAt, mergedLastResearchedAt)
                    .SetProperty(company => company.UpdatedAt, mergedAt), cancellationToken);
            await dbContext.Companies
                .Where(company => company.Id == duplicate.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            canonical.Website = mergedWebsite;
            canonical.Country = mergedCountry;
            canonical.LegalName = mergedLegalName;
            canonical.RegistrationNumber = mergedRegistrationNumber;
            canonical.Headquarters = mergedHeadquarters;
            canonical.LastResearchedAt = mergedLastResearchedAt;
            canonical.UpdatedAt = mergedAt;
            return new CompanyMergeResult(
                CompanyMergeOutcome.Merged,
                ToResponse(canonical),
                preview);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<CompanyMergePreviewResponse> BuildPreviewAsync(
        Company canonical,
        Company duplicate,
        CancellationToken cancellationToken)
    {
        var duplicateRunIds = await dbContext.ResearchRuns
            .AsNoTracking()
            .Where(run => run.CompanyId == duplicate.Id)
            .Select(run => run.Id)
            .ToArrayAsync(cancellationToken);
        var duplicateDeepRunIds = await dbContext.DeepResearchRuns
            .AsNoTracking()
            .Where(run => run.CompanyId == duplicate.Id)
            .Select(run => run.Id)
            .ToArrayAsync(cancellationToken);
        var duplicateProfileCandidateIds = await dbContext.CompanyProfileCandidates
            .AsNoTracking()
            .Where(profile => profile.CompanyId == duplicate.Id)
            .Select(profile => profile.Id)
            .ToArrayAsync(cancellationToken);
        var duplicateProfileVersionIds = await dbContext.CompanyProfileVersions
            .AsNoTracking()
            .Where(profile => profile.CompanyId == duplicate.Id)
            .Select(profile => profile.Id)
            .ToArrayAsync(cancellationToken);
        var duplicateSources = await dbContext.SourceDocuments
            .AsNoTracking()
            .Where(source => source.CompanyId == duplicate.Id)
            .ToListAsync(cancellationToken);
        var canonicalSources = await dbContext.SourceDocuments
            .AsNoTracking()
            .Where(source => source.CompanyId == canonical.Id)
            .ToListAsync(cancellationToken);
        var sourceMap = BuildSourceMap(duplicateSources, canonicalSources);

        return new CompanyMergePreviewResponse(
            canonical.Id,
            duplicate.Id,
            canonical.Name,
            duplicate.Name,
            duplicateRunIds.Length,
            await CountByRunIdsAsync(dbContext.ResearchCandidates, candidate => candidate.ResearchRunId, duplicateRunIds, cancellationToken),
            await CountByRunIdsAsync(dbContext.ResearchEvents, researchEvent => researchEvent.ResearchRunId, duplicateRunIds, cancellationToken),
            await CountByRunIdsAsync(dbContext.ResearchIdentityCandidates, candidate => candidate.ResearchRunId, duplicateRunIds, cancellationToken),
            duplicateSources.Count,
            sourceMap.Count,
            duplicateSources.Count - sourceMap.Count,
            duplicateProfileCandidateIds.Length,
            duplicateProfileVersionIds.Length,
            await dbContext.ProfileEvidences.CountAsync(evidence =>
                (evidence.CompanyProfileCandidateId.HasValue && duplicateProfileCandidateIds.Contains(evidence.CompanyProfileCandidateId.Value)) ||
                (evidence.CompanyProfileVersionId.HasValue && duplicateProfileVersionIds.Contains(evidence.CompanyProfileVersionId.Value)), cancellationToken),
            await dbContext.ProfileChanges.CountAsync(change => change.CompanyId == duplicate.Id, cancellationToken),
            duplicateDeepRunIds.Length,
            await CountByIdsAsync(dbContext.DeepResearchActivities, activity => activity.DeepResearchRunId, duplicateDeepRunIds, cancellationToken),
            await dbContext.SavedResearchArtifacts.CountAsync(artifact => artifact.CompanyId == duplicate.Id, cancellationToken),
            await dbContext.CompanyMonitoringSettings.AnyAsync(setting => setting.CompanyId == canonical.Id, cancellationToken),
            await dbContext.CompanyMonitoringSettings.AnyAsync(setting => setting.CompanyId == duplicate.Id, cancellationToken),
            BuildWarnings(sourceMap.Count, duplicateSources.Count - sourceMap.Count,
                await dbContext.CompanyMonitoringSettings.AnyAsync(setting => setting.CompanyId == canonical.Id, cancellationToken),
                await dbContext.CompanyMonitoringSettings.AnyAsync(setting => setting.CompanyId == duplicate.Id, cancellationToken)));
    }








}
