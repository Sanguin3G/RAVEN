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
using Day6IdentitySnapshot = Raven.Api.Features.Research.Identity.ResolvedIdentitySnapshot;
using Day6IdentitySnapshotSerializer = Raven.Api.Features.Research.Identity.ResolvedIdentitySnapshotSerializer;
using Raven.Api.Features.Search;
using Raven.Api.Features.Settings;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research.Routing;
using Raven.Api.Features.Research.Coverage;

namespace Raven.Api.Features.Research;

public sealed partial class ResearchCompanyService(
    RavenDbContext dbContext,
    ISearchProvider searchProvider,
    ICrawlerProvider crawlerProvider,
    SourceCandidateSelector candidateSelector,
    SourceUrlNormalizer urlNormalizer,
    IResearchEventWriter eventWriter,
    IResearchExecutionContext executionContext,
    ISourceSemanticReranker? sourceSemanticReranker = null,
    IResearchSettingsService? researchSettings = null,
    ICompanyProfilePersistenceService? profilePersistence = null,
    CoverageAwareSourceSelector? coverageAwareSourceSelector = null,
    TargetedQueryPlanner? targetedQueryPlanner = null,
    OfficialSiteEvidencePlanner? officialSiteEvidencePlanner = null,
    IResearchRunConfigurationSnapshot? configurationSnapshot = null,
    IResearchTelemetryFlusher? telemetryFlusher = null,
    ILogger<ResearchCompanyService>? logger = null,
    ResearchDiscoveryCoordinator? discoveryCoordinator = null,
    ResearchEvidenceAcquirer? evidenceAcquirer = null) : IResearchCompanyService
{
    private const int MaximumRecommendedCandidates = 5;
    private const int SearchResultsPerQuery = 5;

    private readonly IResearchRunConfigurationSnapshot? configurationSnapshot = configurationSnapshot;
    private readonly ResearchDiscoveryCoordinator sourceDiscovery = discoveryCoordinator ?? new(
        dbContext,
        searchProvider,
        candidateSelector,
        urlNormalizer,
        eventWriter,
        executionContext,
        sourceSemanticReranker,
        coverageAwareSourceSelector,
        targetedQueryPlanner,
        officialSiteEvidencePlanner);
    private readonly ResearchEvidenceAcquirer evidenceAcquirer = evidenceAcquirer ?? new(
        dbContext,
        crawlerProvider,
        urlNormalizer,
        eventWriter,
        executionContext);

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
            ResolvedIdentitySnapshotJson = Day6IdentitySnapshotSerializer.Serialize(request?.ResolvedIdentity),
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
                ResolvedIdentitySnapshotJson = Day6IdentitySnapshotSerializer.Serialize(request?.ResolvedIdentity)
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
                Day6IdentitySnapshotSerializer.Deserialize(run.ResolvedIdentitySnapshotJson),
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

    public async Task<IReadOnlyList<SourceDocumentResponse>?> ListSourcesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Companies.AnyAsync(company => company.Id == companyId, cancellationToken))
        {
            return null;
        }

        return await ReadSourceResponsesAsync(
            dbContext.SourceDocuments.AsNoTracking().Where(source => source.CompanyId == companyId),
            cancellationToken);
    }

    public async Task<IReadOnlyList<SourceDocumentResponse>?> ListRunSourcesAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.ResearchRuns.AnyAsync(run => run.Id == researchRunId, cancellationToken))
        {
            return null;
        }

        return await ReadSourceResponsesAsync(
            dbContext.SourceDocuments.AsNoTracking().Where(source => source.ResearchRunId == researchRunId),
            cancellationToken);
    }

    public async Task<SourceDocumentDetailResponse?> GetSourceAsync(Guid sourceId, CancellationToken cancellationToken) =>
        await dbContext.SourceDocuments.AsNoTracking()
            .Where(source => source.Id == sourceId)
            .Select(source => new SourceDocumentDetailResponse(
                source.Id,
                source.CompanyId,
                source.ResearchRunId,
                source.Url,
                source.NormalizedUrl,
                source.Title,
                source.SourceDomain,
                source.SourceKind,
                source.IconUrl,
                source.StructuredFactsJson,
                source.RetrievedAt,
                source.Content,
                source.ContentHash,
                source.CrawlerProvider))
            .SingleOrDefaultAsync(cancellationToken);

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
        Day6IdentitySnapshot? resolvedIdentity,
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

    private async Task<IReadOnlyList<SourceDocumentResponse>> ReadSourceResponsesAsync(
        IQueryable<SourceDocument> query,
        CancellationToken cancellationToken)
    {
        var sources = await query.ToListAsync(cancellationToken);
        return sources.OrderByDescending(source => source.RetrievedAt).Select(source => new SourceDocumentResponse(
            source.Id,
            source.CompanyId,
            source.ResearchRunId,
            source.Url,
            source.Title,
            source.SourceDomain,
            source.SourceKind,
            source.IconUrl,
            source.RetrievedAt,
            source.CrawlerProvider,
            source.Content.Length <= 500 ? source.Content : source.Content[..500])).ToArray();
    }

    private async Task<ResearchRunResponse> FailAsync(
        ResearchRun run,
        string error,
        CancellationToken cancellationToken)
    {
        run.Status = ResearchRunStatus.Failed;
        run.Stage = ResearchStage.Failed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Error = error[..Math.Min(error.Length, 4_000)];
        await dbContext.SaveChangesAsync(cancellationToken);
        await FlushTelemetryAsync(cancellationToken);
        return ToResponse(run);
    }

    private Task WriteEventAsync(
        ResearchRun run,
        ResearchEventCategory category,
        ResearchEventStatus status,
        string? provider,
        string? outputSummary,
        CancellationToken cancellationToken) =>
        eventWriter.WriteAsync(new ResearchEvent
        {
            ResearchRunId = run.Id,
            Stage = run.Stage,
            Category = category,
            Status = status,
            Provider = provider,
            OutputSummary = outputSummary
        }, cancellationToken);

    private async Task FlushTelemetryAsync(CancellationToken cancellationToken)
    {
        if (telemetryFlusher is null) return;
        try
        {
            await telemetryFlusher.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(exception, "Could not flush research execution telemetry; the run remains successful.");
        }
    }


    private static string? SerializeTopCvFacts(TopCvParsedFacts facts) =>
        facts == TopCvParsedFacts.Empty ||
        (facts.RegistrationNumber is null && facts.EmployeeCountRange is null && facts.Industry is null && facts.Address is null && facts.Introduction is null)
            ? null
            : JsonSerializer.Serialize(facts);

    private static string? SerializeMaSoThueFacts(MaSoThueParsedFacts facts) =>
        facts == MaSoThueParsedFacts.Empty ||
        (facts.LegalName is null && facts.TaxId is null && facts.InternationalName is null &&
         facts.Representative is null && facts.RegisteredAddress is null && facts.Status is null &&
         facts.RegisteredBusinessActivities.Count == 0)
            ? null
            : JsonSerializer.Serialize(facts);

    private static ResearchCandidateResponse ToResponse(ResearchCandidate candidate)
    {
        var recommendation = ParseRecommendation(candidate.RecommendationReasonsJson);
        return new ResearchCandidateResponse(
            candidate.Id,
            candidate.ResearchRunId,
            candidate.Url,
            candidate.NormalizedUrl,
            candidate.Domain,
            candidate.Title,
            candidate.Snippet,
            candidate.SourceKind,
            recommendation.Reasons,
            candidate.Recommended,
            candidate.Selected,
            candidate.AcquisitionStatus,
            candidate.AcquisitionError,
            candidate.IconUrl,
            candidate.DiscoveredAt,
            recommendation.EntityRelationship,
            recommendation.SemanticRelevance,
            recommendation.SemanticPurposes,
            recommendation.SemanticRationale);
    }

    private static ResearchRunResponse ToResponse(ResearchRun run) =>
        new(
            run.Id,
            run.CompanyId,
            run.Status,
            run.RequestedSearchProvider,
            run.ActualSearchProvider,
            run.RequestedCrawlerProvider,
            run.ActualCrawlerProvider,
            run.SourcesFound,
            run.SourcesSelected,
            run.SourcesCrawled,
            run.StartedAt,
            run.CompletedAt,
            run.Error,
            run.Stage,
            run.ResearchHint,
            run.QueriesTotal,
            run.QueriesCompleted,
            run.UniqueCandidates,
            run.RecommendedCandidates,
            run.CrawlTotal,
            run.CrawlCompleted,
            run.CrawlSucceeded,
            run.CrawlFailed,
            run.DocumentsAdded,
            run.DuplicatesSkipped,
            run.GroundingMode,
            run.ResolvedIdentityCandidateId,
            run.Mode,
            run.BaseProfileVersionId,
            GetTargets(run),
            Day6IdentitySnapshotSerializer.Deserialize(run.ResolvedIdentitySnapshotJson));

    private static IReadOnlyList<ResearchTarget> GetTargets(ResearchRun run)
    {
        try
        {
            return string.IsNullOrWhiteSpace(run.ResearchTargetsJson)
                ? []
                : JsonSerializer.Deserialize<ResearchTarget[]>(run.ResearchTargetsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string SerializeTargets(IReadOnlyList<ResearchTarget>? targets) =>
        JsonSerializer.Serialize((targets ?? []).Distinct().Take(TargetedQueryPlanner.MaximumTargetsPerRound).ToArray());

    private async Task<ResearchSettingsResponse?> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        if (researchSettings is null)
        {
            return null;
        }

        try
        {
            var settings = await researchSettings.GetAsync(cancellationToken);
            configurationSnapshot?.Set(settings);
            return settings;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A settings-store outage must not make deterministic research
            // unavailable. The workflow will use its safe in-memory defaults.
            return null;
        }
    }

    private static CandidateRecommendationPayload ParseRecommendation(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CandidateRecommendationPayload([], null, null, [], null);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return new CandidateRecommendationPayload(
                    JsonSerializer.Deserialize<string[]>(document.RootElement.GetRawText()) ?? [],
                    null,
                    null,
                    [],
                    null);
            }

            var parsed = JsonSerializer.Deserialize<CandidateRecommendationPayload>(
                document.RootElement.GetRawText(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });
            return parsed ?? new CandidateRecommendationPayload([], null, null, [], null);
        }
        catch (JsonException)
        {
            return new CandidateRecommendationPayload([], null, null, [], null);
        }
    }

    private static string Quote(string? value) =>
        $"\"{(value ?? string.Empty).Trim().Replace("\"", " ", StringComparison.Ordinal)}\"";

    private static string? TrimOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<Guid> DeserializeGuidArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Guid[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? BuildIconUrl(string? domain) =>
        string.IsNullOrWhiteSpace(domain) ? null : $"https://{domain.Trim().ToLowerInvariant()}/favicon.ico";

    private static string BuildFailureMessage(string prefix, IEnumerable<string> errors)
    {
        var detail = string.Join(" | ", errors.Where(error => !string.IsNullOrWhiteSpace(error)).Take(3));
        return string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix} {detail}";
    }

    private sealed record CandidateRecommendationPayload(
        IReadOnlyList<string> Reasons,
        EntityRelationship? EntityRelationship,
        CandidateRelevance? SemanticRelevance,
        IReadOnlyList<string> SemanticPurposes,
        string? SemanticRationale);
}
