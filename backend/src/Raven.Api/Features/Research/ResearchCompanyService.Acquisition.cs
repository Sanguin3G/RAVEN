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
    public async Task<ResearchRunResponse?> AcquireAsync(
        Guid researchRunId,
        AcquireResearchCandidatesRequest request,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ResearchRuns
            .SingleOrDefaultAsync(candidate => candidate.Id == researchRunId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        if (request is null || request.CandidateIds is null || request.CandidateIds.Count == 0)
        {
            throw new BadHttpRequestException("Select at least one discovered source before acquiring.");
        }

        var selectedIds = request.CandidateIds.Distinct().ToHashSet();
        var candidates = await dbContext.ResearchCandidates
            .Where(candidate => candidate.ResearchRunId == researchRunId)
            .ToListAsync(cancellationToken);
        if (selectedIds.Any(id => candidates.All(candidate => candidate.Id != id)))
        {
            throw new BadHttpRequestException("One or more selected sources do not belong to this research run.");
        }

        foreach (var candidate in candidates)
        {
            candidate.Selected = selectedIds.Contains(candidate.Id);
        }

        var selectedCandidates = candidates
            .Where(candidate => candidate.Selected)
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();
        run.Stage = ResearchStage.Acquiring;
        run.Status = ResearchRunStatus.Crawling;
        run.ActualCrawlerProvider = null;
        run.SourcesSelected = selectedCandidates.Length;
        run.CrawlTotal = selectedCandidates.Length;
        run.CrawlCompleted = 0;
        run.CrawlSucceeded = 0;
        run.CrawlFailed = 0;
        run.DocumentsAdded = 0;
        run.DuplicatesSkipped = 0;
        run.SourcesCrawled = 0;
        run.CompletedAt = null;
        run.Error = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        var acquisition = await evidenceAcquirer.AcquireAsync(run, selectedCandidates, cancellationToken);
        var errors = acquisition.Errors.ToList();
        if (acquisition.FatalError is not null)
        {
            return await FailAsync(run, acquisition.FatalError, cancellationToken);
        }

        if (run.CrawlSucceeded == 0)
        {
            return await FailAsync(
                run,
                BuildFailureMessage("No selected URLs could be crawled.", errors),
                cancellationToken);
        }

        run.Status = ResearchRunStatus.Completed;
        run.Stage = ResearchStage.EvidenceReady;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Error = errors.Count == 0 ? null : BuildFailureMessage("Some selected URLs could not be crawled.", errors);
        var company = await dbContext.Companies
            .SingleAsync(candidate => candidate.Id == run.CompanyId, cancellationToken);
        company.LastResearchedAt = run.CompletedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        await FlushTelemetryAsync(cancellationToken);
        return ToResponse(run);
    }

    public async Task<ResearchRunResponse?> VerifySourceLeadsAsync(
        Guid companyId,
        VerifyResearchSourceLeadsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (companyId == Guid.Empty || string.IsNullOrWhiteSpace(request.Objective))
        {
            throw new ArgumentException("A company and research objective are required.");
        }

        var urls = (request.Urls ?? [])
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToArray();
        if (urls.Length == 0)
        {
            throw new BadHttpRequestException("Select at least one source lead to verify.");
        }

        var company = await dbContext.Companies.SingleOrDefaultAsync(item => item.Id == companyId, cancellationToken);
        if (company is null)
        {
            return null;
        }

        var normalizedCandidates = new List<ResearchCandidate>();
        var normalizedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
            {
                throw new BadHttpRequestException("Source leads must use HTTP or HTTPS URLs.");
            }

            var normalized = urlNormalizer.Normalize(url);
            if (string.IsNullOrWhiteSpace(normalized) || !normalizedUrls.Add(normalized))
            {
                continue;
            }

            normalizedCandidates.Add(new ResearchCandidate
            {
                Url = url,
                NormalizedUrl = normalized,
                Domain = parsed.Host,
                Title = parsed.Host,
                Snippet = "Selected source lead from an investigation.",
                SourceKind = SourceKind.ExternalWebsite,
                SearchRank = normalizedCandidates.Count + 1,
                Score = 100,
                RecommendationReasonsJson = JsonSerializer.Serialize(new[] { "Selected investigation source lead" }),
                Recommended = true,
                Selected = true,
                IconUrl = BuildIconUrl(parsed.Host)
            });
        }

        if (normalizedCandidates.Count == 0)
        {
            throw new BadHttpRequestException("No usable source leads were supplied.");
        }

        var settings = await ReadSettingsAsync(cancellationToken);
        var run = new ResearchRun
        {
            CompanyId = companyId,
            RequestedSearchProvider = settings?.SearchProviderPriority.FirstOrDefault() ?? searchProvider.Id,
            RequestedCrawlerProvider = settings?.CrawlerProviderPriority.FirstOrDefault() ?? crawlerProvider.Id,
            ResearchHint = request.Objective.Trim(),
            GroundingMode = settings?.GroundingMode ?? GroundingMode.Auto,
            Mode = ResearchMode.TargetedEnrichment,
            BaseProfileVersionId = request.BaseProfileVersionId,
            ResearchTargetsJson = SerializeTargets(request.Targets),
            Stage = ResearchStage.AwaitingSourceSelection,
            Status = ResearchRunStatus.Searching,
            SourcesFound = normalizedCandidates.Count,
            UniqueCandidates = normalizedCandidates.Count,
            RecommendedCandidates = normalizedCandidates.Count
        };
        dbContext.ResearchRuns.Add(run);
        foreach (var candidate in normalizedCandidates)
        {
            dbContext.ResearchCandidates.Add(new ResearchCandidate
            {
                Id = candidate.Id,
                ResearchRunId = run.Id,
                Url = candidate.Url,
                NormalizedUrl = candidate.NormalizedUrl,
                Domain = candidate.Domain,
                Title = candidate.Title,
                Snippet = candidate.Snippet,
                SourceKind = candidate.SourceKind,
                SearchRank = candidate.SearchRank,
                Score = candidate.Score,
                RecommendationReasonsJson = candidate.RecommendationReasonsJson,
                Recommended = candidate.Recommended,
                Selected = candidate.Selected,
                IconUrl = candidate.IconUrl
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await AcquireAsync(
            run.Id,
            new AcquireResearchCandidatesRequest(normalizedCandidates.Select(candidate => candidate.Id).ToArray()),
            cancellationToken);
    }
}
