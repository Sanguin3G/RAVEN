namespace Raven.Api.Features.Companies.Workspace;

/// <summary>
/// Builds a read-only, deterministic workspace review. An optional AI provider
/// can add suggestions after deterministic analysis; it cannot mutate a Company
/// or issue merge/archive/delete commands.
/// </summary>
public sealed class CompanyWorkspaceReviewService : ICompanyWorkspaceReviewService
{
    private readonly ICompanyHealthEvaluator healthEvaluator;
    private readonly ICompanyDuplicateGroupingService duplicateGroupingService;
    private readonly IWorkspaceReviewAiProvider? aiProvider;

    public CompanyWorkspaceReviewService(
        ICompanyHealthEvaluator? healthEvaluator = null,
        ICompanyDuplicateGroupingService? duplicateGroupingService = null,
        IWorkspaceReviewAiProvider? aiProvider = null)
    {
        this.healthEvaluator = healthEvaluator ?? new CompanyHealthEvaluator();
        this.duplicateGroupingService = duplicateGroupingService ?? new CompanyDuplicateGroupingService();
        this.aiProvider = aiProvider;
    }

    public WorkspaceReviewResponse Review(WorkspaceReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var records = NormalizeRecords(request);
        var duplicateGroups = duplicateGroupingService.Group(records);
        var duplicateIds = duplicateGroups
            .SelectMany(group => group.Members)
            .Select(member => member.CompanyId)
            .ToHashSet();

        var companies = records
            .Select(snapshot => new WorkspaceReviewCompany(
                snapshot.Company.Id,
                snapshot.Company.Name,
                healthEvaluator.Evaluate(snapshot, duplicateIds.Contains(snapshot.Company.Id), request.AsOf)))
            .OrderBy(company => company.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(company => company.CompanyId)
            .ToArray();

        var recommendations = BuildRecommendations(companies, duplicateGroups);
        return new WorkspaceReviewResponse(companies, duplicateGroups, recommendations);
    }

    public async Task<WorkspaceReviewResponse> ReviewAsync(
        WorkspaceReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var deterministic = Review(request);
        if (!request.IncludeAiSuggestions || aiProvider is null)
        {
            return deterministic;
        }

        WorkspaceReviewAiResult? aiResult;
        try
        {
            aiResult = await aiProvider.ReviewAsync(
                new WorkspaceReviewAiRequest(
                    deterministic.Companies,
                    deterministic.DuplicateGroups,
                    deterministic.Recommendations),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return deterministic with
            {
                AiWarning = "AI workspace review was unavailable; deterministic findings are still available."
            };
        }

        if (aiResult is null)
        {
            return deterministic with
            {
                AiWarning = "AI workspace review returned no result; deterministic findings are still available."
            };
        }

        var validCompanyIds = deterministic.Companies
            .Select(company => company.CompanyId)
            .ToHashSet();
        var aiRecommendations = aiResult.Suggestions
            .Where(suggestion => validCompanyIds.Contains(suggestion.CompanyId))
            .Select(suggestion => new WorkspaceReviewRecommendation(
                suggestion.Kind,
                suggestion.CompanyId,
                Bound(suggestion.Title, 200, "RAVEN review suggestion"),
                Bound(suggestion.Summary, 1_000, "Review the suggested workspace relationship."),
                (suggestion.RelatedCompanyIds ?? [])
                    .Where(validCompanyIds.Contains)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray(),
                (suggestion.Reasons ?? [])
                    .Where(reason => !string.IsNullOrWhiteSpace(reason))
                    .Select(reason => reason.Trim())
                    .Take(8)
                    .ToArray(),
                null,
                true))
            .ToArray();

        var recommendations = deterministic.Recommendations
            .Concat(aiRecommendations)
            .OrderBy(recommendation => recommendation.Kind)
            .ThenBy(recommendation => recommendation.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(recommendation => recommendation.CompanyId)
            .ToArray();

        return deterministic with
        {
            Recommendations = recommendations,
            AiUsed = aiRecommendations.Length > 0,
            AiWarning = aiResult.Warning
        };
    }

    private static IReadOnlyList<CompanyWorkspaceSnapshot> NormalizeRecords(WorkspaceReviewRequest request)
    {
        return request.Companies
            .Where(snapshot => snapshot is not null)
            .Where(snapshot => request.IncludeArchived || !snapshot.IsArchived)
            .GroupBy(snapshot => snapshot.Company.Id)
            .Select(group => group
                .OrderByDescending(snapshot => snapshot.AcceptedProfile is not null)
                .ThenByDescending(snapshot => snapshot.Company.UpdatedAt)
                .First())
            .OrderBy(snapshot => snapshot.Company.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.Company.Id)
            .ToArray();
    }

    private static IReadOnlyList<WorkspaceReviewRecommendation> BuildRecommendations(
        IReadOnlyList<WorkspaceReviewCompany> companies,
        IReadOnlyList<CompanyDuplicateGroup> duplicateGroups)
    {
        var recommendations = new List<WorkspaceReviewRecommendation>();
        foreach (var group in duplicateGroups)
        {
            var company = group.Members[0];
            recommendations.Add(new WorkspaceReviewRecommendation(
                WorkspaceReviewRecommendationKind.PossibleDuplicate,
                company.CompanyId,
                "Possible duplicate records",
                $"RAVEN found {group.Members.Count} records with matching company identity signals.",
                group.Members.Select(member => member.CompanyId).ToArray(),
                [group.Rationale]));
        }

        foreach (var company in companies)
        {
            var health = company.Health;
            var recommendation = health.BaseStatus switch
            {
                CompanyHealthStatus.Unresearched => CreateHealthRecommendation(
                    WorkspaceReviewRecommendationKind.Unresearched,
                    company,
                    "Company has not been researched",
                    "No accepted profile or research evidence is recorded."),
                CompanyHealthStatus.Sparse => CreateHealthRecommendation(
                    WorkspaceReviewRecommendationKind.SparseProfile,
                    company,
                    "Sparse company profile",
                    "The accepted dossier has too little supported coverage for a healthy workspace record."),
                _ => null
            };

            if (recommendation is not null)
            {
                recommendations.Add(recommendation);
            }

            if (health.Flags.Contains(CompanyHealthStatus.Stale))
            {
                recommendations.Add(CreateHealthRecommendation(
                    WorkspaceReviewRecommendationKind.Stale,
                    company,
                    "Profile may be outdated",
                    "The last research run is outside the configured freshness window."));
            }

            if (health.Flags.Contains(CompanyHealthStatus.PendingUpdate))
            {
                recommendations.Add(CreateHealthRecommendation(
                    WorkspaceReviewRecommendationKind.PendingUpdate,
                    company,
                    "Pending research update",
                    "A refresh or monitoring run is ready for review."));
            }
        }

        return recommendations
            .OrderBy(recommendation => recommendation.Kind)
            .ThenBy(recommendation => recommendation.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(recommendation => recommendation.CompanyId)
            .ToArray();
    }

    private static WorkspaceReviewRecommendation CreateHealthRecommendation(
        WorkspaceReviewRecommendationKind kind,
        WorkspaceReviewCompany company,
        string title,
        string summary) => new(
        kind,
        company.CompanyId,
        title,
        summary,
        [],
        company.Health.Reasons,
        company.Health.Status);

    private static string Bound(string? value, int maxLength, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized[..Math.Min(maxLength, normalized.Length)];
    }
}
