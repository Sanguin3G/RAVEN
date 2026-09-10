using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Research;

public sealed class ResearchCompanyService(
    RavenDbContext dbContext,
    ISearchProvider searchProvider,
    ICrawlerProvider crawlerProvider,
    SourceCandidateSelector candidateSelector) : IResearchCompanyService
{
    public async Task<ResearchRunResponse?> ResearchAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies.SingleOrDefaultAsync(candidate => candidate.Id == companyId, cancellationToken);
        if (company is null)
        {
            return null;
        }

        var run = new ResearchRun
        {
            CompanyId = company.Id,
            RequestedSearchProvider = searchProvider.Id,
            RequestedCrawlerProvider = crawlerProvider.Id
        };
        dbContext.ResearchRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            run.ActualSearchProvider = searchProvider.Id;
            var searchResults = new List<SearchResult>();
            foreach (var query in BuildQueries(company.Name))
            {
                var search = await searchProvider.SearchAsync(new SearchRequest(query, 5), cancellationToken);
                searchResults.AddRange(search.Results);
            }

            run.SourcesFound = searchResults.Count;
            var candidates = candidateSelector.Select(company, searchResults);
            run.SourcesSelected = candidates.Count;
            if (candidates.Count == 0)
            {
                return await FailAsync(run, "Search completed but returned no useful source URLs.", cancellationToken);
            }

            run.Status = ResearchRunStatus.Crawling;
            run.ActualCrawlerProvider = crawlerProvider.Id;
            await dbContext.SaveChangesAsync(cancellationToken);

            var contentHashes = (await dbContext.SourceDocuments.AsNoTracking()
                    .Where(source => source.CompanyId == company.Id)
                    .Select(source => source.ContentHash)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            var errors = new List<string>();
            foreach (var candidate in candidates)
            {
                try
                {
                    var crawl = await crawlerProvider.CrawlAsync(new CrawlRequest(candidate.NormalizedUrl), cancellationToken);
                    if (!crawl.Success || string.IsNullOrWhiteSpace(crawl.Markdown))
                    {
                        errors.Add($"{candidate.NormalizedUrl}: {crawl.Error ?? "crawl failed"}");
                        continue;
                    }

                    run.SourcesCrawled++;
                    var contentHash = HashContent(crawl.Markdown);
                    if (!contentHashes.Add(contentHash))
                    {
                        continue;
                    }

                    dbContext.SourceDocuments.Add(new SourceDocument
                    {
                        CompanyId = company.Id,
                        ResearchRunId = run.Id,
                        Url = crawl.FinalUrl ?? candidate.NormalizedUrl,
                        NormalizedUrl = candidate.NormalizedUrl,
                        Title = crawl.Title ?? candidate.Title,
                        SourceDomain = candidate.SourceDomain,
                        RetrievedAt = crawl.RetrievedAt,
                        Content = crawl.Markdown,
                        ContentHash = contentHash,
                        CrawlerProvider = crawl.Provider
                    });
                }
                catch (ProviderException exception) when (exception.Kind is ProviderFailureKind.Configuration or ProviderFailureKind.Authentication)
                {
                    return await FailAsync(run, exception.Message, cancellationToken);
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    errors.Add($"{candidate.NormalizedUrl}: crawler unavailable");
                }
            }

            if (run.SourcesCrawled == 0)
            {
                return await FailAsync(run, BuildFailureMessage("No selected URLs could be crawled.", errors), cancellationToken);
            }

            run.Status = ResearchRunStatus.Completed;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Error = errors.Count == 0 ? null : BuildFailureMessage("Some selected URLs could not be crawled.", errors);
            company.LastResearchedAt = run.CompletedAt;
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToResponse(run);
        }
        catch (ProviderException exception)
        {
            return await FailAsync(run, exception.Message, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return await FailAsync(run, "Research provider could not be reached.", cancellationToken);
        }
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
            .Select(run => ToResponse(run))
            .ToListAsync(cancellationToken);

        return runs.OrderByDescending(run => run.StartedAt).ToArray();
    }

    public async Task<IReadOnlyList<SourceDocumentResponse>?> ListSourcesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Companies.AnyAsync(company => company.Id == companyId, cancellationToken))
        {
            return null;
        }

        var sources = await dbContext.SourceDocuments.AsNoTracking()
            .Where(source => source.CompanyId == companyId)
            .Select(source => new SourceDocumentResponse(
                source.Id,
                source.CompanyId,
                source.ResearchRunId,
                source.Url,
                source.Title,
                source.SourceDomain,
                source.RetrievedAt,
                source.CrawlerProvider,
                source.Content.Length <= 500 ? source.Content : source.Content.Substring(0, 500)))
            .ToListAsync(cancellationToken);

        return sources.OrderByDescending(source => source.RetrievedAt).ToArray();
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
                source.RetrievedAt,
                source.Content,
                source.ContentHash,
                source.CrawlerProvider))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<ResearchRunResponse> FailAsync(ResearchRun run, string error, CancellationToken cancellationToken)
    {
        run.Status = ResearchRunStatus.Failed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Error = error[..Math.Min(error.Length, 4_000)];
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(run);
    }

    private static IReadOnlyList<string> BuildQueries(string name) =>
    [
        $"\"{name}\" official",
        $"\"{name}\" products services",
        $"\"{name}\" locations markets"
    ];

    private static string HashContent(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static string BuildFailureMessage(string prefix, IEnumerable<string> errors)
    {
        var detail = string.Join(" | ", errors.Take(3));
        return string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix} {detail}";
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
            run.Error);
}
