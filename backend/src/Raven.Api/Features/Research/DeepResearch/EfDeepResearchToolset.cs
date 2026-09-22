using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.DeepResearch;

/// <summary>
/// Read-only adapter over the existing company/profile/source/provider
/// boundaries. It never calls a profile or company mutation method and does
/// not persist crawled pages; page acquisition remains an explicit future
/// application workflow.
/// </summary>
public sealed class EfDeepResearchToolset(
    RavenDbContext dbContext,
    ICompanyProfilePersistenceService profilePersistence,
    ISearchProvider searchProvider,
    ICrawlerProvider crawlerProvider) : IDeepResearchToolset
{
    private const int MaxSourceContentCharacters = 8_000;

    public bool SupportsStoredSourceTextSearch => true;

    public async Task<DeepResearchToolResult<DeepResearchProfile>> GetCompanyProfileAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return new DeepResearchToolResult<DeepResearchProfile>(false, null, "A company ID is required.");
        }

        var profile = await profilePersistence.GetCurrentProfileAsync(companyId, cancellationToken);
        if (profile is null)
        {
            return new DeepResearchToolResult<DeepResearchProfile>(true, null, "No accepted profile is available.");
        }

        return new DeepResearchToolResult<DeepResearchProfile>(
            true,
            new DeepResearchProfile(
                companyId,
                profile.Version,
                profile.DisplayName,
                profile.LegalName,
                profile.Website,
                profile.Country,
                profile.Headquarters,
                profile.PrimaryIndustry,
                profile.Summary),
            null);
    }

    public async Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>> GetCompanySourcesAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return new DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>(false, null, "A company ID is required.");
        }

        var sources = await dbContext.SourceDocuments
            .AsNoTracking()
            .Where(source => source.CompanyId == companyId)
            .OrderByDescending(source => source.RetrievedAt)
            .Take(DeepResearchBudget.Defaults.MaxDocuments)
            .ToListAsync(cancellationToken);
        return SourceResult(sources);
    }

    public async Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSearchHit>>> SearchWebAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new DeepResearchToolResult<IReadOnlyList<DeepResearchSearchHit>>(false, null, "A search query is required.");
        }

        var response = await searchProvider.SearchAsync(
            new SearchRequest(query.Trim()[..Math.Min(500, query.Trim().Length)], Math.Clamp(maxResults, 1, 10)),
            cancellationToken);
        return new DeepResearchToolResult<IReadOnlyList<DeepResearchSearchHit>>(
            true,
            response.Results
                .Take(10)
                .Select(result => new DeepResearchSearchHit(result.Title, result.Url, result.Snippet, result.Rank))
                .ToArray(),
            null,
            response.Provider);
    }

    public async Task<DeepResearchToolResult<DeepResearchCrawlPage>> CrawlPageAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            return new DeepResearchToolResult<DeepResearchCrawlPage>(false, null, "Only absolute HTTP(S) URLs can be crawled.");
        }

        var result = await crawlerProvider.CrawlAsync(new CrawlRequest(parsed.ToString()), cancellationToken);
        if (!result.Success)
        {
            return new DeepResearchToolResult<DeepResearchCrawlPage>(false, null, "The page could not be read.", result.Provider);
        }

        var page = new DeepResearchCrawlPage(
            parsed.ToString(),
            result.FinalUrl,
            result.Title,
            Bound(result.Markdown, MaxSourceContentCharacters),
            result.Provider,
            result.RetrievedAt);
        return new DeepResearchToolResult<DeepResearchCrawlPage>(
            true,
            page,
            null,
            result.Provider);
    }

    public async Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>> SearchStoredSourceTextAsync(
        Guid companyId,
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || string.IsNullOrWhiteSpace(query))
        {
            return new DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>(false, null, "A company ID and search query are required.");
        }

        var pattern = $"%{query.Trim().Replace("%", "[%]").Replace("_", "[_]")}%";
        var sources = await dbContext.SourceDocuments
            .AsNoTracking()
            .Where(source => source.CompanyId == companyId && EF.Functions.Like(source.Content, pattern))
            .OrderByDescending(source => source.RetrievedAt)
            .Take(Math.Clamp(maxResults, 1, DeepResearchBudget.Defaults.MaxDocuments))
            .ToListAsync(cancellationToken);
        return SourceResult(sources);
    }

    private static DeepResearchToolResult<IReadOnlyList<DeepResearchSource>> SourceResult(IReadOnlyList<SourceDocument> sources)
    {
        var mapped = sources.Select(source => new DeepResearchSource(
            source.Id,
            source.Url,
            source.Title,
            source.SourceKind.ToString(),
            Bound(source.Content, MaxSourceContentCharacters),
            source.RetrievedAt)).ToArray();
        return new DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>(
            true,
            mapped,
            null,
            sources.FirstOrDefault()?.CrawlerProvider,
            mapped.Select(source => source.SourceDocumentId).ToArray());
    }

    private static string Bound(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized[..Math.Min(maxLength, normalized.Length)];
    }
}
