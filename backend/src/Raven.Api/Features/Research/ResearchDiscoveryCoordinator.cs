using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Planning;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Research;

/// <summary>
/// Owns deterministic source discovery policy: provider search, official-site
/// planning, classification/ranking, optional semantic reranking, and candidate
/// persistence. The public research service coordinates the run lifecycle around it.
/// </summary>
public sealed class ResearchDiscoveryCoordinator(
    RavenDbContext dbContext,
    ISearchProvider searchProvider,
    SourceCandidateSelector candidateSelector,
    SourceUrlNormalizer urlNormalizer,
    IResearchEventWriter eventWriter,
    IResearchExecutionContext executionContext,
    ISourceSemanticReranker? sourceSemanticReranker = null,
    CoverageAwareSourceSelector? coverageAwareSourceSelector = null,
    TargetedQueryPlanner? targetedQueryPlanner = null,
    OfficialSiteEvidencePlanner? officialSiteEvidencePlanner = null)
{
    private const int MaximumRecommendedCandidates = 5;
    private const int SearchResultsPerQuery = 5;

    private readonly SourceClassifier sourceClassifier = new();
    private readonly OfficialSiteDiscoveryPlanner officialSitePlanner = new(urlNormalizer);
    private readonly CoverageAwareSourceSelector coverageSourceSelector = coverageAwareSourceSelector ?? new();
    private readonly TargetedQueryPlanner targetedPlanner = targetedQueryPlanner ?? new();
    private readonly OfficialSiteEvidencePlanner officialEvidencePlanner = officialSiteEvidencePlanner ?? new(urlNormalizer);

    public Task<(List<SearchResult> Results, List<string> Errors)> SearchAsync(
        ResearchRun run,
        ResearchIdentityInput identity,
        CancellationToken cancellationToken)
        => SearchQueriesAsync(run, identity, BuildQueries(identity, run.Mode, GetTargets(run)), true, cancellationToken);

    private async Task<(List<SearchResult> Results, List<string> Errors)> SearchQueriesAsync(
        ResearchRun run,
        ResearchIdentityInput identity,
        IReadOnlyList<string> queries,
        bool resetCounters,
        CancellationToken cancellationToken)
    {
        if (resetCounters)
        {
            run.ActualSearchProvider = null;
            run.QueriesTotal = queries.Count;
            run.QueriesCompleted = 0;
            run.SourcesFound = 0;
        }
        else
        {
            run.QueriesTotal += queries.Count;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        var searchResults = new List<SearchResult>();
        var errors = new List<string>();
        foreach (var query in queries)
        {
            try
            {
                using var telemetryScope = executionContext.Push(run.Id, stage: run.Stage);
                var search = await searchProvider.SearchAsync(
                    new SearchRequest(query, SearchResultsPerQuery, identity.Country),
                    cancellationToken);
                run.ActualSearchProvider = search.Provider;
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

    public CandidateDraft[] CreateDrafts(IReadOnlyList<RankedCandidate> rankedCandidates) =>
        rankedCandidates
            .Select((ranked, index) =>
            {
                var recommended = index < Math.Min(MaximumRecommendedCandidates, rankedCandidates.Count);
                return new CandidateDraft(Guid.NewGuid(), ranked, recommended, recommended);
            })
            .ToArray();

    public RankedCandidate[] RankCandidates(
        Company company,
        IReadOnlyList<SearchResult> searchResults,
        string? officialWebsite,
        IReadOnlyCollection<ResearchTarget>? targets = null)
    {
        var officialLinks = searchResults.Select(result => new OfficialSiteLink(
            result.Url, result.Title, result.Snippet, result.Rank)).ToArray();
        var plannedOfficialCandidates = officialWebsite is null
            ? []
            : targets is { Count: > 0 }
                ? officialEvidencePlanner.Plan(new OfficialSiteEvidenceRequest(officialWebsite, officialLinks, targets))
                    .Select(candidate => new OfficialSiteCandidate(candidate.Url, candidate.NormalizedUrl, candidate.Domain,
                        candidate.Title, candidate.Snippet, candidate.Priority, candidate.RecommendationReasons,
                        candidate.Priority >= 42, candidate.DiscoveryRank)).ToArray()
                : officialSitePlanner.Plan(new OfficialSiteDiscoveryRequest(officialWebsite, officialLinks));

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

    public async Task<CandidateDraft[]> ApplySemanticRerankingAsync(
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

    public void AddCandidateEntities(
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

    private static SourceSemanticCandidate ToSemanticCandidate(CandidateDraft draft) => new(
        draft.Id,
        draft.Ranked.Source.Url,
        draft.Ranked.Classification.Domain ?? draft.Ranked.Source.SourceDomain,
        draft.Ranked.Source.Title,
        draft.Ranked.Source.Snippet,
        draft.Ranked.Classification.SourceKind,
        draft.Ranked.Score,
        draft.Ranked.Reasons);

    private static bool IsRecommended(SourceSemanticAssessment assessment) =>
        assessment.Recommended && assessment.EntityRelationship is not EntityRelationship.DifferentEntity && assessment.Relevance is not CandidateRelevance.Low;

    public IReadOnlyList<string> BuildQueries(
        ResearchIdentityInput input,
        ResearchMode mode,
        IReadOnlyCollection<ResearchTarget> targets)
    {
        if (mode == ResearchMode.TargetedEnrichment && targets.Count > 0)
        {
            return targetedPlanner.Plan(input, targets);
        }

        return BuildInitialQueries(input);
    }

    private static IReadOnlyList<string> BuildInitialQueries(ResearchIdentityInput input)
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

    public static string? ResolveOfficialWebsite(Company company, IEnumerable<SearchResult> results)
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
        SourceKind.OfficialBusinessRegistry => 900,
        SourceKind.BusinessDirectory => 760,
        SourceKind.TopCv => 700,
        SourceKind.BusinessRegistry => 650,
        SourceKind.LinkedIn => 500,
        SourceKind.News => 300,
        SourceKind.ExternalWebsite => 100,
        _ => 0
    };

    private static string Quote(string? value) =>
        $"\"{(value ?? string.Empty).Trim().Replace("\"", " ", StringComparison.Ordinal)}\"";

    public CandidateDraft[] ApplyCoverageAwareSelection(
        IReadOnlyList<CandidateDraft> drafts,
        IReadOnlyCollection<ResearchTarget> targets)
    {
        var candidates = drafts.Select(draft => new CoverageSourceCandidate(
            draft.Id,
            draft.Ranked.Source.Url,
            draft.Ranked.Source.Title,
            draft.Ranked.Source.Snippet,
            draft.Ranked.Classification.SourceKind,
            draft.Ranked.Score,
            draft.Ranked.Source.SearchRank,
            draft.Assessment?.EntityRelationship ?? EntityRelationship.SameEntity,
            draft.Assessment?.Relevance ?? CandidateRelevance.High,
            draft.Assessment?.Purposes,
            draft.Ranked.Reasons,
            draft.Assessment?.Recommended ?? draft.Recommended,
            draft.Assessment?.Rationale,
            draft.Ranked.Classification.SourceKind is SourceKind.OfficialWebsite or SourceKind.OfficialDocument,
            draft.Ranked.Source.NormalizedUrl)).ToArray();
        var selection = coverageSourceSelector.Select(new CoverageSourceSelectionRequest(candidates, targets));
        var byId = selection.RecommendedRoots.ToDictionary(root => root.Candidate.CandidateId);
        return drafts.Select(draft =>
        {
            if (!byId.TryGetValue(draft.Id, out var selected))
            {
                return draft with { Recommended = false };
            }

            var ranked = draft.Ranked with
            {
                Reasons = draft.Ranked.Reasons.Concat(selected.Reasons)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
            return draft with { Ranked = ranked, Recommended = true };
        }).ToArray();
    }

    private static string SerializeRecommendation(
        IReadOnlyList<string> reasons,
        SourceSemanticAssessment? assessment)
    {
        if (assessment is null)
        {
            // Keep the original array format for candidates that were only
            // deterministically ranked; this remains compatible with older data.
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

    private static string? BuildIconUrl(string? domain) =>
        string.IsNullOrWhiteSpace(domain) ? null : $"https://{domain.Trim().ToLowerInvariant()}/favicon.ico";

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
}

public sealed record RankedCandidate(
    SourceCandidate Source,
    SourceClassificationResult Classification,
    int Score,
    IReadOnlyList<string> Reasons);

public sealed record CandidateDraft(
    Guid Id,
    RankedCandidate Ranked,
    bool DeterministicRecommended,
    bool Recommended,
    SourceSemanticAssessment? Assessment = null);

internal sealed record CandidateRecommendationPayload(
    IReadOnlyList<string> Reasons,
    EntityRelationship? EntityRelationship,
    CandidateRelevance? SemanticRelevance,
    IReadOnlyList<string> SemanticPurposes,
    string? SemanticRationale);
