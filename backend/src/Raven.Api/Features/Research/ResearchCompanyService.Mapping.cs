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
        ResolvedIdentitySnapshotSerializer.Deserialize(run.ResolvedIdentitySnapshotJson));

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
