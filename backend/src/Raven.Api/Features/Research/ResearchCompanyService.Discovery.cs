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
    public async Task<ResearchRunResponse?> ResearchAsync(Guid companyId, CancellationToken cancellationToken)
    {
        // Compatibility callers retain the deterministic one-call workflow.
        // The staged workflow uses the same coordinator when it needs grounding.
        var discovered = await DiscoverAsync(companyId, new DiscoverResearchRequest(GroundingMode: GroundingMode.Off), cancellationToken);
        if (discovered is null || discovered.Stage is ResearchStage.Failed or ResearchStage.AwaitingIdentitySelection)
        {
            return discovered;
        }

        var recommendedIds = await dbContext.ResearchCandidates
            .Where(candidate => candidate.ResearchRunId == discovered.Id && candidate.Recommended)
            .OrderByDescending(candidate => candidate.Score)
            .Select(candidate => candidate.Id)
            .ToArrayAsync(cancellationToken);

        return recommendedIds.Length == 0
            ? discovered
            : await AcquireAsync(
                discovered.Id,
                new AcquireResearchCandidatesRequest(recommendedIds),
                cancellationToken);
    }

    public async Task<ResearchRunResponse?> DiscoverAsync(
        Guid companyId,
        DiscoverResearchRequest? request,
        CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies
            .SingleOrDefaultAsync(candidate => candidate.Id == companyId, cancellationToken);
        if (company is null)
        {
            return null;
        }

        ResearchRun? run = request?.ResearchRunId is { } queuedRunId
            ? await dbContext.ResearchRuns.SingleOrDefaultAsync(item => item.Id == queuedRunId && item.CompanyId == companyId, cancellationToken)
            : null;
        if (request?.ResearchRunId is not null && run is null) return null;
        if (run?.Status == ResearchRunStatus.Cancelled) return ToResponse(run);
        if (run is null)
        {
            var persistedSettings = await ReadSettingsAsync(cancellationToken);
            run = new ResearchRun
            {
                CompanyId = company.Id,
                RequestedSearchProvider = persistedSettings?.SearchProviderPriority.FirstOrDefault() ?? searchProvider.Id,
                RequestedCrawlerProvider = persistedSettings?.CrawlerProviderPriority.FirstOrDefault() ?? crawlerProvider.Id,
                ResearchHint = TrimOptional(request?.ResearchHint),
                GroundingMode = request?.GroundingMode ?? persistedSettings?.GroundingMode ?? GroundingMode.Auto,
                Mode = request?.Mode ?? ResearchMode.Initial,
                BaseProfileVersionId = request?.BaseProfileVersionId,
                ResearchTargetsJson = SerializeTargets(request?.Targets),
                ResolvedIdentitySnapshotJson = ResolvedIdentitySnapshotSerializer.Serialize(request?.ResolvedIdentity)
            };
            dbContext.ResearchRuns.Add(run);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        run.Stage = ResearchStage.Discovering;
        run.Status = ResearchRunStatus.Searching;
        run.CompletedAt = null;
        run.Error = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var initialIdentity = await BuildInitialIdentityAsync(
                company,
                run.ResearchHint,
                request?.UseAcceptedProfileIdentity == true,
                ResolvedIdentitySnapshotSerializer.Deserialize(run.ResolvedIdentitySnapshotJson),
                cancellationToken);
            var (searchResults, errors) = await sourceDiscovery.SearchAsync(run, initialIdentity, cancellationToken);

            if (searchResults.Count == 0)
            {
                return await FailAsync(
                    run,
                    BuildFailureMessage("Search completed but returned no useful source URLs.", errors),
                    cancellationToken);
            }

            var officialWebsite = ResearchDiscoveryCoordinator.ResolveOfficialWebsite(company, searchResults);
            var rankedCandidates = sourceDiscovery.RankCandidates(company, searchResults, officialWebsite, GetTargets(run));

            if (rankedCandidates.Length == 0)
            {
                return await FailAsync(
                    run,
                    BuildFailureMessage("Search completed but returned no useful source URLs.", errors),
                    cancellationToken);
            }

            var drafts = sourceDiscovery.CreateDrafts(rankedCandidates);
            drafts = sourceDiscovery.ApplyCoverageAwareSelection(drafts, GetTargets(run));

            run.UniqueCandidates = drafts.Length;
            run.RecommendedCandidates = drafts.Count(candidate => candidate.Recommended);
            run.Stage = ResearchStage.AwaitingSourceSelection;
            run.Status = ResearchRunStatus.Searching;
            if (run.Error is null && errors.Count > 0)
            {
                run.Error = BuildFailureMessage("Some search queries failed.", errors);
            }
            sourceDiscovery.AddCandidateEntities(drafts, run.Id, null);
            await dbContext.SaveChangesAsync(cancellationToken);
            await WriteEventAsync(run, ResearchEventCategory.CandidateDiscovery, ResearchEventStatus.WaitingForUser,
                searchProvider.Id, $"Discovered {run.UniqueCandidates} unique candidates; {run.RecommendedCandidates} recommended.", cancellationToken);
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
            logger?.LogError(exception, "Research discovery failed for run {ResearchRunId}", run.Id);
            return await FailAsync(run, "Research failed unexpectedly. Please retry.", cancellationToken);
        }
    }
}
