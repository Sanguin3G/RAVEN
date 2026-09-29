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
}
