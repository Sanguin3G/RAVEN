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
using Raven.Api.Features.Search;
using Raven.Api.Features.Settings;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research.Routing;

namespace Raven.Api.Features.Research;

public sealed class ResearchCompanyService(
    RavenDbContext dbContext,
    ISearchProvider searchProvider,
    ICrawlerProvider crawlerProvider,
    SourceCandidateSelector candidateSelector,
    SourceUrlNormalizer urlNormalizer,
    IResearchEventWriter eventWriter,
    ICompanyIdentityResolver? identityResolver = null,
    ISourceSemanticReranker? sourceSemanticReranker = null,
    IResearchSettingsService? researchSettings = null,
    ICompanyProfilePersistenceService? profilePersistence = null) : IResearchCompanyService
{
    private const int MaximumRecommendedCandidates = 5;
    private const int SearchResultsPerQuery = 5;

    private readonly SourceClassifier sourceClassifier = new();
    private readonly OfficialSiteDiscoveryPlanner officialSitePlanner = new(urlNormalizer);
    private readonly TopCvSourceParser topCvSourceParser = new();
    private readonly IdentityAmbiguityAnalyzer ambiguityAnalyzer = new();

    public async Task<ResearchRunResponse?> ResearchAsync(Guid companyId, CancellationToken cancellationToken)
    {
        // Compatibility callers retain the deterministic Day-3 one-call workflow.
        // The Day-4 React workflow uses the staged endpoint when it needs grounding.
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

        var persistedSettings = await ReadSettingsAsync(cancellationToken);
        var run = new ResearchRun
        {
            CompanyId = company.Id,
            RequestedSearchProvider = persistedSettings?.SearchProviderPriority.FirstOrDefault() ?? searchProvider.Id,
            RequestedCrawlerProvider = persistedSettings?.CrawlerProviderPriority.FirstOrDefault() ?? crawlerProvider.Id,
            ResearchHint = TrimOptional(request?.ResearchHint),
            GroundingMode = request?.GroundingMode ?? persistedSettings?.GroundingMode ?? GroundingMode.Auto
        };
        dbContext.ResearchRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var initialIdentity = await BuildInitialIdentityAsync(
                company,
                run.ResearchHint,
                request?.UseAcceptedProfileIdentity == true,
                cancellationToken);
            var (searchResults, errors) = await SearchAsync(run, initialIdentity, cancellationToken);

            if (searchResults.Count == 0)
            {
                return await FailAsync(
                    run,
                    BuildFailureMessage("Search completed but returned no useful source URLs.", errors),
                    cancellationToken);
            }

            var officialWebsite = ResolveOfficialWebsite(company, searchResults);
            var rankedCandidates = RankCandidates(company, searchResults, officialWebsite);

            if (rankedCandidates.Length == 0)
            {
                return await FailAsync(
                    run,
                    BuildFailureMessage("Search completed but returned no useful source URLs.", errors),
                    cancellationToken);
            }

            var drafts = rankedCandidates
                .Select((ranked, index) =>
                {
                    var recommended = index < Math.Min(MaximumRecommendedCandidates, rankedCandidates.Length);
                    return new CandidateDraft(Guid.NewGuid(), ranked, recommended, recommended);
                })
                .ToArray();
            var settings = await ReadSettingsAsync(cancellationToken);
            var shouldGround = identityResolver is not null &&
                               ambiguityAnalyzer.RequiresGrounding(
                                   new IdentityResolutionRequest(initialIdentity, drafts.Select(ToGroundingCandidate).ToArray()),
                                   run.GroundingMode);
            ResolvedResearchEntity? resolvedTarget = null;
            var identityRecords = Array.Empty<ResearchIdentityCandidate>();

            if (shouldGround)
            {
                run.Stage = ResearchStage.Grounding;
                await dbContext.SaveChangesAsync(cancellationToken);
                await WriteEventAsync(run, ResearchEventCategory.GroundingRequested, ResearchEventStatus.Working,
                    settings?.GroundingModel, "Resolving the intended real-world organization from bounded search metadata.", cancellationToken);

                IdentityResolutionResult resolution;
                try
                {
                    resolution = await identityResolver!.ResolveAsync(
                        new IdentityResolutionRequest(initialIdentity, drafts.Select(ToGroundingCandidate).ToArray()),
                        cancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    resolution = new IdentityResolutionResult(
                        false,
                        null,
                        [],
                        "Company identity grounding failed; deterministic research can continue.",
                        new AiFailure("provider_error", "Company identity grounding failed.", true));
                }
                identityRecords = PersistIdentityCandidates(run, resolution);

                if (resolution.Failure is not null)
                {
                    run.Error = resolution.Warning;
                    await WriteEventAsync(run, ResearchEventCategory.GroundingFailed, ResearchEventStatus.Failed,
                        settings?.GroundingModel, resolution.Warning, cancellationToken);
                }
                else if (resolution.Ambiguous)
                {
                    run.UniqueCandidates = drafts.Length;
                    run.RecommendedCandidates = drafts.Count(candidate => candidate.DeterministicRecommended);
                    run.Stage = ResearchStage.AwaitingIdentitySelection;
                    run.Status = ResearchRunStatus.Searching;
                    run.Error = null;
                    AddCandidateEntities(drafts, run.Id, null);
                    TrackIdentityCandidates(identityRecords);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await WriteEventAsync(run, ResearchEventCategory.GroundingCompleted, ResearchEventStatus.WaitingForUser,
                        settings?.GroundingModel, $"Found {identityRecords.Length} possible research targets; awaiting user selection.", cancellationToken);
                    return ToResponse(run);
                }
                else
                {
                    resolvedTarget = SelectResolvedTarget(resolution, identityRecords);
                    if (resolvedTarget is not null)
                    {
                        var selectedRecord = identityRecords.Single(record =>
                            string.Equals(record.TemporaryId, resolvedTarget.TemporaryId, StringComparison.OrdinalIgnoreCase));
                        selectedRecord.Selected = true;
                        run.ResolvedIdentityCandidateId = selectedRecord.Id;
                        await WriteEventAsync(run, ResearchEventCategory.GroundingCompleted, ResearchEventStatus.Completed,
                            settings?.GroundingModel, $"Selected {resolvedTarget.DisplayName} as the research target.", cancellationToken);
                    }
                }
            }

            if (resolvedTarget is not null && settings?.AiSourceRerankingEnabled == true && sourceSemanticReranker is not null)
            {
                drafts = await ApplySemanticRerankingAsync(run, resolvedTarget, drafts, settings.GroundingModel, cancellationToken);
            }

            run.UniqueCandidates = drafts.Length;
            run.RecommendedCandidates = drafts.Count(candidate => candidate.Recommended);
            run.Stage = ResearchStage.AwaitingSourceSelection;
            run.Status = ResearchRunStatus.Searching;
            if (run.Error is null && errors.Count > 0)
            {
                run.Error = BuildFailureMessage("Some search queries failed.", errors);
            }
            AddCandidateEntities(drafts, run.Id, null);
            TrackIdentityCandidates(identityRecords);
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

        var contentHashes = (await dbContext.SourceDocuments
                .AsNoTracking()
                .Where(source => source.CompanyId == run.CompanyId)
                .Select(source => source.ContentHash)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var candidate in selectedCandidates)
        {
            candidate.AcquisitionStatus = CandidateAcquisitionStatus.Acquiring;
            candidate.AcquisitionError = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            await WriteEventAsync(run, ResearchEventCategory.CrawlRequested, ResearchEventStatus.Working,
                run.RequestedCrawlerProvider, $"Acquiring {candidate.Domain}.", cancellationToken);

            try
            {
                var crawl = await crawlerProvider.CrawlAsync(
                    new CrawlRequest(candidate.NormalizedUrl),
                    cancellationToken);
                run.ActualCrawlerProvider = crawl.Provider;
                await WriteProviderFallbackAsync(run, crawlerProvider, cancellationToken);
                run.CrawlCompleted++;

                if (!crawl.Success || string.IsNullOrWhiteSpace(crawl.Markdown))
                {
                    candidate.AcquisitionStatus = CandidateAcquisitionStatus.Failed;
                    candidate.AcquisitionError = TrimOptional(crawl.Error) ?? "Crawler returned no readable content.";
                    run.CrawlFailed++;
                    errors.Add($"{candidate.NormalizedUrl}: {candidate.AcquisitionError}");
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await WriteEventAsync(run, ResearchEventCategory.CrawlFailed, ResearchEventStatus.Failed,
                        crawl.Provider, $"Could not acquire {candidate.Domain}.", cancellationToken);
                    continue;
                }

                run.SourcesCrawled++;
                run.CrawlSucceeded++;
                var contentHash = HashContent(crawl.Markdown);
                if (!contentHashes.Add(contentHash))
                {
                    candidate.AcquisitionStatus = CandidateAcquisitionStatus.DuplicateSkipped;
                    run.DuplicatesSkipped++;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await WriteEventAsync(run, ResearchEventCategory.DuplicateSkipped, ResearchEventStatus.Skipped,
                        crawl.Provider, $"Duplicate content skipped for {candidate.Domain}.", cancellationToken);
                    continue;
                }

                var sourceKind = candidate.SourceKind;
                var structuredFactsJson = sourceKind == SourceKind.TopCv
                    ? SerializeTopCvFacts(topCvSourceParser.Parse(crawl.Markdown))
                    : null;
                var documentUrl = crawl.FinalUrl ?? candidate.NormalizedUrl;
                var normalizedDocumentUrl = urlNormalizer.Normalize(documentUrl) ?? candidate.NormalizedUrl;

                dbContext.SourceDocuments.Add(new SourceDocument
                {
                    CompanyId = run.CompanyId,
                    ResearchRunId = run.Id,
                    Url = documentUrl,
                    NormalizedUrl = normalizedDocumentUrl,
                    Title = crawl.Title ?? candidate.Title,
                    SourceDomain = candidate.Domain,
                    SourceKind = sourceKind,
                    IconUrl = candidate.IconUrl,
                    StructuredFactsJson = structuredFactsJson,
                    RetrievedAt = crawl.RetrievedAt,
                    Content = crawl.Markdown,
                    ContentHash = contentHash,
                    CrawlerProvider = crawl.Provider
                });
                candidate.AcquisitionStatus = CandidateAcquisitionStatus.Acquired;
                run.DocumentsAdded++;
                await dbContext.SaveChangesAsync(cancellationToken);
                await WriteEventAsync(run, ResearchEventCategory.SourcePersisted, ResearchEventStatus.Completed,
                    crawl.Provider, $"Stored evidence from {candidate.Domain}.", cancellationToken);
            }
            catch (ProviderException exception) when (exception.Kind is ProviderFailureKind.Configuration or ProviderFailureKind.Authentication)
            {
                candidate.AcquisitionStatus = CandidateAcquisitionStatus.Failed;
                candidate.AcquisitionError = exception.Message;
                run.CrawlCompleted++;
                run.CrawlFailed++;
                await dbContext.SaveChangesAsync(cancellationToken);
                return await FailAsync(run, exception.Message, cancellationToken);
            }
            catch (ProviderException exception)
            {
                candidate.AcquisitionStatus = CandidateAcquisitionStatus.Failed;
                candidate.AcquisitionError = exception.Message;
                run.CrawlCompleted++;
                run.CrawlFailed++;
                errors.Add($"{candidate.NormalizedUrl}: {exception.Message}");
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (HttpRequestException)
            {
                candidate.AcquisitionStatus = CandidateAcquisitionStatus.Failed;
                candidate.AcquisitionError = "Crawler could not be reached.";
                run.CrawlCompleted++;
                run.CrawlFailed++;
                errors.Add($"{candidate.NormalizedUrl}: {candidate.AcquisitionError}");
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                candidate.AcquisitionStatus = CandidateAcquisitionStatus.Failed;
                candidate.AcquisitionError = "Crawler timed out.";
                run.CrawlCompleted++;
                run.CrawlFailed++;
                errors.Add($"{candidate.NormalizedUrl}: {candidate.AcquisitionError}");
                await dbContext.SaveChangesAsync(cancellationToken);
            }
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
        await WriteEventAsync(run, ResearchEventCategory.CrawlCompleted, ResearchEventStatus.Completed,
            run.ActualCrawlerProvider ?? run.RequestedCrawlerProvider, $"Acquisition finished: {run.DocumentsAdded} documents added, {run.DuplicatesSkipped} duplicates skipped.", cancellationToken);
        return ToResponse(run);
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
            var (searchResults, errors) = await SearchAsync(run, targetIdentity, cancellationToken);
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
            var officialWebsite = ResolveOfficialWebsite(targetCompany, searchResults);
            var rankedCandidates = RankCandidates(targetCompany, searchResults, officialWebsite);
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
                drafts = await ApplySemanticRerankingAsync(
                    run,
                    resolvedIdentity,
                    drafts,
                    settings.GroundingModel,
                    cancellationToken);
            }

            run.UniqueCandidates = drafts.Length;
            run.RecommendedCandidates = drafts.Count(candidate => candidate.Recommended);
            run.Stage = ResearchStage.AwaitingSourceSelection;
            run.Status = ResearchRunStatus.Searching;
            run.Error = errors.Count == 0 ? null : BuildFailureMessage("Some search queries failed.", errors);
            AddCandidateEntities(drafts, run.Id, null);
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

    private async Task<(List<SearchResult> Results, List<string> Errors)> SearchAsync(
        ResearchRun run,
        ResearchIdentityInput identity,
        CancellationToken cancellationToken)
    {
        run.ActualSearchProvider = null;
        var queries = BuildQueries(identity);
        run.QueriesTotal = queries.Count;
        run.QueriesCompleted = 0;
        run.SourcesFound = 0;
        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteEventAsync(run, ResearchEventCategory.SearchRequested, ResearchEventStatus.Working,
            run.RequestedSearchProvider, $"Planned {queries.Count} bounded public-source queries.", cancellationToken);

        var searchResults = new List<SearchResult>();
        var errors = new List<string>();
        foreach (var query in queries)
        {
            try
            {
                var search = await searchProvider.SearchAsync(
                    new SearchRequest(query, SearchResultsPerQuery, identity.Country),
                    cancellationToken);
                run.ActualSearchProvider = search.Provider;
                await WriteProviderFallbackAsync(run, searchProvider, cancellationToken);
                searchResults.AddRange(search.Results);
            }
            catch (ProviderException exception) when (exception.Kind is ProviderFailureKind.Configuration or ProviderFailureKind.Authentication)
            {
                throw;
            }
            catch (ProviderException exception)
            {
                errors.Add($"{exception.Provider}: {exception.Message}");
            }
            catch (HttpRequestException)
            {
                errors.Add("Search provider could not be reached.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                errors.Add("Search provider timed out.");
            }

            run.QueriesCompleted++;
            run.SourcesFound = searchResults.Count;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return (searchResults, errors);
    }

    private RankedCandidate[] RankCandidates(
        Company company,
        IReadOnlyList<SearchResult> searchResults,
        string? officialWebsite)
    {
        var plannedOfficialCandidates = officialWebsite is null
            ? []
            : officialSitePlanner.Plan(new OfficialSiteDiscoveryRequest(
                officialWebsite,
                searchResults.Select(result => new OfficialSiteLink(
                    result.Url,
                    result.Title,
                    result.Snippet,
                    result.Rank)).ToArray()));

        var plannerResults = plannedOfficialCandidates
            .Select((candidate, index) => new SearchResult(
                candidate.Title,
                candidate.Url,
                candidate.Snippet,
                candidate.DiscoveryRank > 0 ? candidate.DiscoveryRank : index + 1))
            .ToArray();

        var selectorCompany = officialWebsite is null ||
                              string.Equals(officialWebsite, company.Website, StringComparison.OrdinalIgnoreCase)
            ? company
            : new Company
            {
                Name = company.Name,
                LegalName = company.LegalName,
                Website = officialWebsite,
                Country = company.Country,
                RegistrationNumber = company.RegistrationNumber,
                Headquarters = company.Headquarters
            };
        var candidates = candidateSelector.Discover(
                selectorCompany,
                searchResults.Concat(plannerResults))
            .ToArray();

        return candidates
            .Select(candidate =>
            {
                var classification = sourceClassifier.Classify(new SourceClassificationInput(
                    candidate.NormalizedUrl,
                    candidate.Title,
                    candidate.Snippet,
                    officialWebsite));
                var score = candidate.Score + SourceKindBoost(classification.SourceKind);
                var reasons = classification.RecommendationReasons
                    .Concat([candidate.Reason])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new RankedCandidate(candidate, classification, score, reasons);
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Source.SearchRank)
            .ToArray();
    }

    private async Task<CandidateDraft[]> ApplySemanticRerankingAsync(
        ResearchRun run,
        ResolvedResearchEntity target,
        IReadOnlyList<CandidateDraft> drafts,
        string model,
        CancellationToken cancellationToken)
    {
        await WriteEventAsync(run, ResearchEventCategory.SourceSemanticRerankStarted, ResearchEventStatus.Working,
            model, $"Assessing {drafts.Count} source candidates for {target.DisplayName}.", cancellationToken);
        SourceSemanticRerankResult result;
        try
        {
            result = await sourceSemanticReranker!.RerankAsync(
                new SourceSemanticRerankRequest(target, drafts.Select(ToSemanticCandidate).ToArray()),
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            run.Error = "AI source recommendations were unavailable; deterministic ranking remains active.";
            await WriteEventAsync(run, ResearchEventCategory.SourceSemanticRerankCompleted, ResearchEventStatus.Skipped,
                model, run.Error, cancellationToken);
            return drafts.ToArray();
        }
        if (result.Failure is not null || result.Assessments.Count == 0)
        {
            if (result.Warning is not null)
            {
                run.Error = result.Warning;
            }

            await WriteEventAsync(run, ResearchEventCategory.SourceSemanticRerankCompleted, ResearchEventStatus.Skipped,
                model, result.Warning ?? "Semantic source reranking returned no assessments; deterministic ranking remains active.", cancellationToken);
            return drafts.ToArray();
        }

        var validIds = drafts.Select(draft => draft.Id).ToHashSet();
        var assessments = result.Assessments
            .Where(assessment => validIds.Contains(assessment.CandidateId))
            .GroupBy(assessment => assessment.CandidateId)
            .ToDictionary(group => group.Key, group => group.First());
        var reranked = drafts
            .Select(draft => assessments.TryGetValue(draft.Id, out var assessment)
                ? draft with { Assessment = assessment, Recommended = IsRecommended(assessment) }
                : draft)
            .OrderByDescending(draft => draft.Recommended)
            .ThenByDescending(draft => draft.Ranked.Score)
            .ThenBy(draft => draft.Ranked.Source.SearchRank)
            .ToArray();
        await WriteEventAsync(run, ResearchEventCategory.SourceSemanticRerankCompleted, ResearchEventStatus.Completed,
            model, $"Assessed {assessments.Count} source candidates for relevance and entity relationship.", cancellationToken);
        return reranked;
    }

    private ResearchIdentityCandidate[] PersistIdentityCandidates(
        ResearchRun run,
        IdentityResolutionResult resolution)
    {
        var records = resolution.Entities
            .Select(entity => new ResearchIdentityCandidate
            {
                ResearchRunId = run.Id,
                TemporaryId = entity.TemporaryId,
                DisplayName = entity.DisplayName,
                LegalName = entity.LegalName,
                Country = entity.Country,
                Website = entity.Website,
                OfficialDomain = entity.OfficialDomain,
                EntityType = entity.EntityType,
                RelationshipHint = entity.RelationshipHint,
                Confidence = entity.Confidence,
                Rationale = entity.Rationale,
                SupportingCandidateIdsJson = JsonSerializer.Serialize(entity.SupportingCandidateIds.Distinct().ToArray()),
                Recommended = entity.Recommended
            })
            .ToArray();
        return records;
    }

    private void TrackIdentityCandidates(IEnumerable<ResearchIdentityCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            dbContext.Set<ResearchIdentityCandidate>().Add(candidate);
        }
    }

    private void AddCandidateEntities(
        IReadOnlyList<CandidateDraft> drafts,
        Guid researchRunId,
        IReadOnlyDictionary<Guid, SourceSemanticAssessment>? assessments)
    {
        foreach (var draft in drafts)
        {
            var assessment = assessments is not null && assessments.TryGetValue(draft.Id, out var value)
                ? value
                : draft.Assessment;
            dbContext.ResearchCandidates.Add(new ResearchCandidate
            {
                Id = draft.Id,
                ResearchRunId = researchRunId,
                Url = draft.Ranked.Source.Url,
                NormalizedUrl = draft.Ranked.Source.NormalizedUrl,
                Domain = draft.Ranked.Classification.Domain ?? draft.Ranked.Source.SourceDomain,
                Title = draft.Ranked.Source.Title,
                Snippet = draft.Ranked.Source.Snippet,
                SourceKind = draft.Ranked.Classification.SourceKind,
                SearchRank = draft.Ranked.Source.SearchRank,
                Score = draft.Ranked.Score,
                RecommendationReasonsJson = SerializeRecommendation(draft.Ranked.Reasons, assessment),
                Recommended = assessment is null ? draft.Recommended : IsRecommended(assessment),
                IconUrl = BuildIconUrl(draft.Ranked.Classification.Domain ?? draft.Ranked.Source.SourceDomain)
            });
        }
    }

    private static ResolvedResearchEntity? SelectResolvedTarget(
        IdentityResolutionResult resolution,
        IReadOnlyList<ResearchIdentityCandidate> records)
    {
        var temporaryId = resolution.RecommendedTemporaryId ??
                          records.SingleOrDefault(record => record.Recommended)?.TemporaryId;
        return temporaryId is null
            ? null
            : resolution.Entities.SingleOrDefault(entity =>
                string.Equals(entity.TemporaryId, temporaryId, StringComparison.OrdinalIgnoreCase));
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

    private static GroundingSourceCandidate ToGroundingCandidate(CandidateDraft draft) => new(
        draft.Id,
        draft.Ranked.Source.Url,
        draft.Ranked.Classification.Domain ?? draft.Ranked.Source.SourceDomain,
        draft.Ranked.Source.Title,
        draft.Ranked.Source.Snippet,
        draft.Ranked.Classification.SourceKind,
        draft.Ranked.Source.SearchRank,
        draft.Ranked.Reasons,
        draft.Ranked.Classification.SourceKind is SourceKind.OfficialWebsite or SourceKind.OfficialDocument);

    private static SourceSemanticCandidate ToSemanticCandidate(CandidateDraft draft) => new(
        draft.Id,
        draft.Ranked.Source.Url,
        draft.Ranked.Classification.Domain ?? draft.Ranked.Source.SourceDomain,
        draft.Ranked.Source.Title,
        draft.Ranked.Source.Snippet,
        draft.Ranked.Classification.SourceKind,
        draft.Ranked.Score,
        draft.Ranked.Reasons);

    private async Task<ResearchIdentityInput> BuildInitialIdentityAsync(
        Company company,
        string? researchHint,
        bool useAcceptedProfileIdentity,
        CancellationToken cancellationToken)
    {
        if (!useAcceptedProfileIdentity || profilePersistence is null)
        {
            return ResearchIdentityInput.FromCompany(company, researchHint);
        }

        try
        {
            var profile = await profilePersistence.GetCurrentProfileAsync(company.Id, cancellationToken);
            if (profile is null)
            {
                return ResearchIdentityInput.FromCompany(company, researchHint);
            }

            return new ResearchIdentityInput(
                profile.DisplayName?.Trim() is { Length: > 0 } displayName ? displayName : company.Name,
                profile.LegalName?.Trim() is { Length: > 0 } legalName ? legalName : company.LegalName,
                profile.Website?.Trim() is { Length: > 0 } website ? website : company.Website,
                profile.Country?.Trim() is { Length: > 0 } country ? country : company.Country,
                profile.RegistrationNumberOrTaxId?.Trim() is { Length: > 0 } registration ? registration : company.RegistrationNumber,
                profile.Headquarters?.Trim() is { Length: > 0 } headquarters ? headquarters : company.Headquarters,
                researchHint);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Refresh remains available with stable Company identity if old profile data cannot be read.
            return ResearchIdentityInput.FromCompany(company, researchHint);
        }
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
        await WriteEventAsync(run, ResearchEventCategory.CrawlFailed, ResearchEventStatus.Failed,
            null, error, cancellationToken);
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

    private async Task WriteProviderFallbackAsync(
        ResearchRun run,
        object provider,
        CancellationToken cancellationToken)
    {
        if (provider is not IProviderRouteDiagnostics { LastRoute: { } route } || route.Attempts.Count == 0)
        {
            return;
        }

        var attempted = string.Join(", ", route.Attempts.Select(attempt => attempt.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase));
        var summary = string.IsNullOrWhiteSpace(route.ActualProvider)
            ? $"All attempted {route.Capability} providers failed: {attempted}."
            : $"{route.Capability} fell back from {attempted} to {route.ActualProvider}.";
        await WriteEventAsync(run, ResearchEventCategory.ProviderFallback, ResearchEventStatus.Completed,
            route.ActualProvider, summary, cancellationToken);
    }

    private static IReadOnlyList<string> BuildQueries(Company company, string? researchHint) =>
        BuildQueries(ResearchIdentityInput.FromCompany(company, researchHint));

    private static IReadOnlyList<string> BuildQueries(ResearchIdentityInput input)
    {
        var name = Quote(input.Name);
        var country = string.IsNullOrWhiteSpace(input.Country) ? null : Quote(input.Country);
        var identityText = string.Join(' ', new[] { name, country }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var official = CompanyIdentityNormalizer.NormalizeWebsiteHost(input.Website) is { } host
            ? $"site:{host} {name} official company"
            : $"{identityText} official company";

        var queries = new List<string>
        {
            official,
            $"site:topcv.vn/cong-ty {name}",
            $"site:linkedin.com/company {name}",
            $"{identityText} business registration registry"
        };

        if (!string.IsNullOrWhiteSpace(input.RegistrationNumber))
        {
            queries.Add($"{Quote(input.RegistrationNumber)} {name} business registration");
        }
        else if (!string.IsNullOrWhiteSpace(input.LegalName))
        {
            queries.Add($"{Quote(input.LegalName)} {country} business registration");
        }

        if (!string.IsNullOrWhiteSpace(input.ResearchHint))
        {
            queries.Add($"{identityText} {Quote(input.ResearchHint)}");
        }

        return queries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private static string? ResolveOfficialWebsite(Company company, IEnumerable<SearchResult> results)
    {
        if (!string.IsNullOrWhiteSpace(company.Website) &&
            Uri.TryCreate(company.Website, UriKind.Absolute, out var suppliedWebsite) &&
            suppliedWebsite.Scheme is "http" or "https")
        {
            return company.Website;
        }

        var nameTokens = (CompanyIdentityNormalizer.NormalizeName(company.Name) ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .ToArray();
        var domains = results
            .Select(result => Uri.TryCreate(result.Url, UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri is not null)
            .Select(uri => uri!)
            .Where(uri => uri.Scheme is "http" or "https")
            .Where(uri => !uri.Host.Contains("topcv", StringComparison.OrdinalIgnoreCase))
            .Where(uri => !uri.Host.Contains("linkedin", StringComparison.OrdinalIgnoreCase))
            .GroupBy(uri => uri.Host, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Host = group.Key,
                Score = group.Count() + group.Sum(uri => nameTokens.Count(token => uri.Host.Contains(token, StringComparison.OrdinalIgnoreCase)))
            })
            .OrderByDescending(group => group.Score)
            .FirstOrDefault();

        return domains is null ? null : $"https://{domains.Host}";
    }

    private static int SourceKindBoost(SourceKind kind) => kind switch
    {
        SourceKind.OfficialWebsite => 1_000,
        SourceKind.OfficialDocument => 950,
        SourceKind.TopCv => 700,
        SourceKind.BusinessRegistry => 650,
        SourceKind.LinkedIn => 500,
        SourceKind.News => 300,
        SourceKind.ExternalWebsite => 100,
        _ => 0
    };

    private static string? SerializeTopCvFacts(TopCvParsedFacts facts) =>
        facts == TopCvParsedFacts.Empty ||
        (facts.RegistrationNumber is null && facts.EmployeeCountRange is null && facts.Industry is null && facts.Address is null && facts.Introduction is null)
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
            run.ResolvedIdentityCandidateId);

    private async Task<ResearchSettingsResponse?> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        if (researchSettings is null)
        {
            return null;
        }

        try
        {
            return await researchSettings.GetAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A settings-store outage must not make deterministic Day-3 research
            // unavailable. The workflow will use its safe in-memory defaults.
            return null;
        }
    }

    private static string SerializeRecommendation(
        IReadOnlyList<string> reasons,
        SourceSemanticAssessment? assessment)
    {
        if (assessment is null)
        {
            // Keep the original array format for candidates that were only
            // deterministically ranked; this remains compatible with Day 3 data.
            return JsonSerializer.Serialize(reasons);
        }

        return JsonSerializer.Serialize(new CandidateRecommendationPayload(
            reasons,
            assessment.EntityRelationship,
            assessment.Relevance,
            assessment.Purposes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            assessment.Rationale),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
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

    private static string HashContent(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static string BuildFailureMessage(string prefix, IEnumerable<string> errors)
    {
        var detail = string.Join(" | ", errors.Where(error => !string.IsNullOrWhiteSpace(error)).Take(3));
        return string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix} {detail}";
    }

    private sealed record RankedCandidate(
        SourceCandidate Source,
        SourceClassificationResult Classification,
        int Score,
        IReadOnlyList<string> Reasons);

    private sealed record CandidateDraft(
        Guid Id,
        RankedCandidate Ranked,
        bool DeterministicRecommended,
        bool Recommended,
        SourceSemanticAssessment? Assessment = null);

    private sealed record CandidateRecommendationPayload(
        IReadOnlyList<string> Reasons,
        EntityRelationship? EntityRelationship,
        CandidateRelevance? SemanticRelevance,
        IReadOnlyList<string> SemanticPurposes,
        string? SemanticRationale);
}
