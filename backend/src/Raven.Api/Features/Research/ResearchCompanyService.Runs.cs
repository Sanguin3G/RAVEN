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
    public async Task<ResearchRunResponse?> CreateQueuedRunAsync(
        Guid companyId,
        DiscoverResearchRequest? request,
        CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies.SingleOrDefaultAsync(item => item.Id == companyId, cancellationToken);
        if (company is null) return null;
        var settings = await ReadSettingsAsync(cancellationToken);
        var run = new ResearchRun
        {
            CompanyId = company.Id,
            RequestedSearchProvider = settings?.SearchProviderPriority.FirstOrDefault() ?? searchProvider.Id,
            RequestedCrawlerProvider = settings?.CrawlerProviderPriority.FirstOrDefault() ?? crawlerProvider.Id,
            ResearchHint = TrimOptional(request?.ResearchHint),
            GroundingMode = request?.GroundingMode ?? settings?.GroundingMode ?? GroundingMode.Auto,
            Mode = request?.Mode ?? ResearchMode.Initial,
            BaseProfileVersionId = request?.BaseProfileVersionId,
            ResearchTargetsJson = SerializeTargets(request?.Targets),
            ResolvedIdentitySnapshotJson = ResolvedIdentitySnapshotSerializer.Serialize(request?.ResolvedIdentity),
            Stage = ResearchStage.Identifying,
            Status = ResearchRunStatus.Searching
        };
        dbContext.ResearchRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(run);
    }

    public async Task<ResearchRunResponse?> CancelAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        var run = await dbContext.ResearchRuns.SingleOrDefaultAsync(item => item.Id == researchRunId, cancellationToken);
        if (run is null) return null;
        // EvidenceReady and AwaitingProfileConfirmation use Completed as the
        // acquisition/generation operation status, but the user still owns the
        // workflow. They remain cancellable until the profile is confirmed and
        // the run reaches the terminal Completed stage.
        if (run.Stage is ResearchStage.Completed or ResearchStage.Failed or ResearchStage.Cancelled) return ToResponse(run);
        run.Status = ResearchRunStatus.Cancelled;
        run.Stage = ResearchStage.Cancelled;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Error = "Research cancelled by the user.";
        await dbContext.SaveChangesAsync(cancellationToken);
        await FlushTelemetryAsync(cancellationToken);
        return ToResponse(run);
    }

    public async Task<IReadOnlyList<ActiveResearchRunResponse>> ListActiveRunsAsync(CancellationToken cancellationToken)
    {
        var runs = await dbContext.ResearchRuns.AsNoTracking()
            .Where(run => run.Status == ResearchRunStatus.Searching || run.Status == ResearchRunStatus.Crawling)
            .Where(run => run.Stage == ResearchStage.Identifying || run.Stage == ResearchStage.Discovering || run.Stage == ResearchStage.Grounding || run.Stage == ResearchStage.Acquiring || run.Stage == ResearchStage.GeneratingProfile)
            .Join(dbContext.Companies.AsNoTracking(), run => run.CompanyId, company => company.Id, (run, company) => new ActiveResearchRunResponse(ToResponse(run), company.Name))
            .ToListAsync(cancellationToken);
        return runs.OrderByDescending(item => item.Run.StartedAt).ToArray();
    }

    public async Task<ResearchRunResponse?> GetRunAsync(Guid researchRunId, CancellationToken cancellationToken) =>
        await dbContext.ResearchRuns.AsNoTracking()
            .Where(run => run.Id == researchRunId)
            .Select(run => ToResponse(run))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ResearchRunResponse>> ListRunsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var runs = await dbContext.ResearchRuns.AsNoTracking()
            .Where(run => run.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        return runs.OrderByDescending(run => run.StartedAt).Select(ToResponse).ToArray();
    }

    public async Task<IReadOnlyList<ResearchCandidateResponse>?> ListCandidatesAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.ResearchRuns.AnyAsync(run => run.Id == researchRunId, cancellationToken))
        {
            return null;
        }

        var candidates = await dbContext.ResearchCandidates.AsNoTracking()
            .Where(candidate => candidate.ResearchRunId == researchRunId)
            .OrderByDescending(candidate => candidate.Recommended)
            .ThenByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.SearchRank)
            .ToListAsync(cancellationToken);
        return candidates.Select(ToResponse).ToArray();
    }
}
