using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Parsing;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Research;

/// <summary>
/// Acquires selected research candidates and persists source evidence. It owns
/// crawler/provider failure handling, duplicate-content suppression, structured
/// source parsing, and acquisition telemetry; run-stage transitions remain with
/// the facade.
/// </summary>
public sealed class ResearchEvidenceAcquirer(
    RavenDbContext dbContext,
    ICrawlerProvider crawlerProvider,
    SourceUrlNormalizer urlNormalizer,
    IResearchEventWriter eventWriter,
    IResearchExecutionContext executionContext)
{
    private readonly TopCvSourceParser topCvSourceParser = new();
    private readonly MaSoThueSourceParser maSoThueSourceParser = new();
    private readonly MaSoThueSourceDetector maSoThueSourceDetector = new();

    public async Task<ResearchAcquisitionResult> AcquireAsync(
        ResearchRun run,
        IReadOnlyList<ResearchCandidate> selectedCandidates,
        CancellationToken cancellationToken)
    {
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
            try
            {
                using var telemetryScope = executionContext.Push(run.Id, stage: run.Stage);
                var crawl = await crawlerProvider.CrawlAsync(
                    new CrawlRequest(candidate.NormalizedUrl),
                    cancellationToken);
                run.ActualCrawlerProvider = crawl.Provider;
                run.CrawlCompleted++;

                if (!crawl.Success || string.IsNullOrWhiteSpace(crawl.Markdown))
                {
                    candidate.AcquisitionStatus = CandidateAcquisitionStatus.Failed;
                    candidate.AcquisitionError = TrimOptional(crawl.Error) ?? "Crawler returned no readable content.";
                    run.CrawlFailed++;
                    errors.Add($"{candidate.NormalizedUrl}: {candidate.AcquisitionError}");
                    await dbContext.SaveChangesAsync(cancellationToken);
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
                var structuredFactsJson = sourceKind switch
                {
                    SourceKind.TopCv => SerializeTopCvFacts(topCvSourceParser.Parse(crawl.Markdown)),
                    SourceKind.BusinessDirectory when maSoThueSourceDetector.IsCompanyUrl(candidate.NormalizedUrl)
                        => SerializeMaSoThueFacts(maSoThueSourceParser.Parse(crawl.Markdown)),
                    _ => null
                };
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
                return new ResearchAcquisitionResult(errors, exception.Message);
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


        return new ResearchAcquisitionResult(errors, null);
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

    private static string? TrimOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string HashContent(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}

public sealed record ResearchAcquisitionResult(
    IReadOnlyList<string> Errors,
    string? FatalError);
