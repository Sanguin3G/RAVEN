using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Companies.Lifecycle;

/// <summary>Deletes a company and its restrictively-related records inside the caller's transaction.</summary>
public sealed class CompanyDeletionService(RavenDbContext dbContext)
{
    public async Task<int> DeleteAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var runIds = await dbContext.ResearchRuns.Where(run => run.CompanyId == companyId).Select(run => run.Id).ToArrayAsync(cancellationToken);
        var deepRunIds = await dbContext.DeepResearchRuns.Where(run => run.CompanyId == companyId).Select(run => run.Id).ToArrayAsync(cancellationToken);
        var candidateIds = await dbContext.CompanyProfileCandidates.Where(profile => profile.CompanyId == companyId).Select(profile => profile.Id).ToArrayAsync(cancellationToken);
        var versionIds = await dbContext.CompanyProfileVersions.Where(profile => profile.CompanyId == companyId).Select(profile => profile.Id).ToArrayAsync(cancellationToken);
        var deleted = 0;
        deleted += await dbContext.ProfileEvidences.Where(item => (item.CompanyProfileCandidateId.HasValue && candidateIds.Contains(item.CompanyProfileCandidateId.Value)) || (item.CompanyProfileVersionId.HasValue && versionIds.Contains(item.CompanyProfileVersionId.Value))).ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.ProfileChanges.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.CompanyProfileCandidates.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.CompanyProfileVersions.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync(cancellationToken);
        if (runIds.Length > 0)
        {
            deleted += await dbContext.ResearchEvents.Where(item => item.ResearchRunId.HasValue && runIds.Contains(item.ResearchRunId.Value)).ExecuteDeleteAsync(cancellationToken);
            deleted += await dbContext.ResearchIdentityCandidates.Where(item => runIds.Contains(item.ResearchRunId)).ExecuteDeleteAsync(cancellationToken);
            deleted += await dbContext.ResearchCandidates.Where(item => runIds.Contains(item.ResearchRunId)).ExecuteDeleteAsync(cancellationToken);
        }
        deleted += await dbContext.SourceDocuments.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.InvestigationOrganizationRevisions.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.ExternalResearchAnalysisJobs.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.SavedResearchArtifacts.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync(cancellationToken);
        if (deepRunIds.Length > 0) deleted += await dbContext.DeepResearchActivities.Where(item => deepRunIds.Contains(item.DeepResearchRunId)).ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.DeepResearchRuns.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.CompanyMonitoringSettings.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.WorkspaceResearchReviewStates.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.ResearchRuns.Where(item => item.CompanyId == companyId).ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.Companies.Where(item => item.Id == companyId).ExecuteDeleteAsync(cancellationToken);
        return deleted;
    }
}
