using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research.Parsing;
using Raven.Api.Features.Research.Planning;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Research;

public sealed class ResearchCompanyService(
    RavenDbContext dbContext,
    ISearchProvider searchProvider,
    ICrawlerProvider crawlerProvider,
    SourceCandidateSelector candidateSelector,
    SourceUrlNormalizer urlNormalizer,
    IResearchEventWriter eventWriter) : IResearchCompanyService
{
    private const int MaximumRecommendedCandidates = 5;
    private const int SearchResultsPerQuery = 5;

    private readonly SourceClassifier sourceClassifier = new();
    private readonly OfficialSiteDiscoveryPlanner officialSitePlanner = new(urlNormalizer);
    private readonly TopCvSourceParser topCvSourceParser = new();

    public async Task<ResearchRunResponse?> ResearchAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var discovered = await DiscoverAsync(companyId, null, cancellationToken);
        if (discovered is null || discovered.Stage is ResearchStage.Failed)
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

        var run = new ResearchRun
        {
            CompanyId = company.Id,
            RequestedSearchProvider = searchProvider.Id,
            RequestedCrawlerProvider = crawlerProvider.Id,
            ResearchHint = TrimOptional(request?.ResearchHint)
        };
        dbContext.ResearchRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            run.ActualSearchProvider = searchProvider.Id;
            var queries = BuildQueries(company, run.ResearchHint);
            run.QueriesTotal = queries.Count;
            await dbContext.SaveChangesAsync(cancellationToken);
            await WriteEventAsync(run, ResearchEventCategory.SearchRequested, ResearchEventStatus.Working,
                searchProvider.Id, $"Planned {queries.Count} bounded public-source queries.", cancellationToken);

            var searchResults = new List<SearchResult>();
            var errors = new List<string>();
            foreach (var query in queries)
            {
                try
                {
                    var search = await searchProvider.SearchAsync(
                        new SearchRequest(query, SearchResultsPerQuery, company.Country),
                        cancellationToken);
                    searchResults.AddRange(search.Results);
                }
                catch (ProviderException exception) when (exception.Kind is ProviderFailureKind.Configuration or ProviderFailureKind.Authentication)
                {
                    return await FailAsync(run, exception.Message, cancellationToken);
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

            if (searchResults.Count == 0)
            {
                return await FailAsync(
                    run,
                    BuildFailureMessage("Search completed but returned no useful source URLs.", errors),
                    cancellationToken);
            }

            var officialWebsite = ResolveOfficialWebsite(company, searchResults);
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
                : new Company { Name = company.Name, Website = officialWebsite };
            var candidates = candidateSelector.Discover(
                    selectorCompany,
                    searchResults.Concat(plannerResults))
                .ToArray();

            if (candidates.Length == 0)
            {
                return await FailAsync(
                    run,
                    BuildFailureMessage("Search completed but returned no useful source URLs.", errors),
                    cancellationToken);
            }

            var rankedCandidates = candidates
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

            var recommendedCount = Math.Min(MaximumRecommendedCandidates, rankedCandidates.Length);
            for (var index = 0; index < rankedCandidates.Length; index++)
            {
                var ranked = rankedCandidates[index];
                var candidate = new ResearchCandidate
                {
                    ResearchRunId = run.Id,
                    Url = ranked.Source.Url,
                    NormalizedUrl = ranked.Source.NormalizedUrl,
                    Domain = ranked.Classification.Domain ?? ranked.Source.SourceDomain,
                    Title = ranked.Source.Title,
                    Snippet = ranked.Source.Snippet,
                    SourceKind = ranked.Classification.SourceKind,
                    SearchRank = ranked.Source.SearchRank,
                    Score = ranked.Score,
                    RecommendationReasonsJson = JsonSerializer.Serialize(ranked.Reasons),
                    Recommended = index < recommendedCount,
                    IconUrl = BuildIconUrl(ranked.Classification.Domain ?? ranked.Source.SourceDomain)
                };
                dbContext.ResearchCandidates.Add(candidate);
            }

            run.UniqueCandidates = rankedCandidates.Length;
            run.RecommendedCandidates = recommendedCount;
            run.Stage = ResearchStage.AwaitingSourceSelection;
            // Status is the legacy broad state. Stage is authoritative for the
            // staged workflow and tells the UI that user input is now required.
            run.Status = ResearchRunStatus.Searching;
            run.Error = errors.Count == 0 ? null : BuildFailureMessage("Some search queries failed.", errors);
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
        run.ActualCrawlerProvider = crawlerProvider.Id;
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
                crawlerProvider.Id, $"Acquiring {candidate.Domain}.", cancellationToken);

            try
            {
                var crawl = await crawlerProvider.CrawlAsync(
                    new CrawlRequest(candidate.NormalizedUrl),
                    cancellationToken);
                run.CrawlCompleted++;

                if (!crawl.Success || string.IsNullOrWhiteSpace(crawl.Markdown))
                {
                    candidate.AcquisitionStatus = CandidateAcquisitionStatus.Failed;
                    candidate.AcquisitionError = TrimOptional(crawl.Error) ?? "Crawler returned no readable content.";
                    run.CrawlFailed++;
                    errors.Add($"{candidate.NormalizedUrl}: {candidate.AcquisitionError}");
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await WriteEventAsync(run, ResearchEventCategory.CrawlFailed, ResearchEventStatus.Failed,
                        crawlerProvider.Id, $"Could not acquire {candidate.Domain}.", cancellationToken);
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
                        crawlerProvider.Id, $"Duplicate content skipped for {candidate.Domain}.", cancellationToken);
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
                    crawlerProvider.Id, $"Stored evidence from {candidate.Domain}.", cancellationToken);
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
            crawlerProvider.Id, $"Acquisition finished: {run.DocumentsAdded} documents added, {run.DuplicatesSkipped} duplicates skipped.", cancellationToken);
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

    private static IReadOnlyList<string> BuildQueries(Company company, string? researchHint)
    {
        var name = Quote(company.Name);
        var country = string.IsNullOrWhiteSpace(company.Country) ? null : Quote(company.Country);
        var identity = string.Join(' ', new[] { name, country }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var official = CompanyIdentityNormalizer.NormalizeWebsiteHost(company.Website) is { } host
            ? $"site:{host} {name} official company"
            : $"{identity} official company";

        var queries = new List<string>
        {
            official,
            $"site:topcv.vn/cong-ty {name}",
            $"site:linkedin.com/company {name}",
            $"{identity} business registration registry"
        };

        if (!string.IsNullOrWhiteSpace(company.RegistrationNumber))
        {
            queries.Add($"{Quote(company.RegistrationNumber)} {name} business registration");
        }
        else if (!string.IsNullOrWhiteSpace(company.LegalName))
        {
            queries.Add($"{Quote(company.LegalName)} {country} business registration");
        }

        if (!string.IsNullOrWhiteSpace(researchHint))
        {
            queries.Add($"{identity} {Quote(researchHint)}");
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
        var reasons = string.IsNullOrWhiteSpace(candidate.RecommendationReasonsJson)
            ? []
            : JsonSerializer.Deserialize<string[]>(candidate.RecommendationReasonsJson) ?? [];
        return new ResearchCandidateResponse(
            candidate.Id,
            candidate.ResearchRunId,
            candidate.Url,
            candidate.NormalizedUrl,
            candidate.Domain,
            candidate.Title,
            candidate.Snippet,
            candidate.SourceKind,
            reasons,
            candidate.Recommended,
            candidate.Selected,
            candidate.AcquisitionStatus,
            candidate.AcquisitionError,
            candidate.IconUrl,
            candidate.DiscoveredAt);
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
            run.DuplicatesSkipped);

    private static string Quote(string? value) =>
        $"\"{(value ?? string.Empty).Trim().Replace("\"", " ", StringComparison.Ordinal)}\"";

    private static string? TrimOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
}
