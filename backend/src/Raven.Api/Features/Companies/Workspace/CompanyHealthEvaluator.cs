using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Profiles;

namespace Raven.Api.Features.Companies.Workspace;

/// <summary>
/// Computes a deterministic, explainable workspace health state from accepted
/// profile coverage and research lifecycle signals. It never writes entities.
/// </summary>
public sealed class CompanyHealthEvaluator : ICompanyHealthEvaluator
{
    private readonly CompanyHealthOptions options;

    public CompanyHealthEvaluator(CompanyHealthOptions? options = null)
    {
        this.options = options ?? new CompanyHealthOptions();
        this.options.Validate();
    }

    public CompanyHealthAssessment Evaluate(
        CompanyWorkspaceSnapshot snapshot,
        bool possibleDuplicate = false,
        DateTimeOffset? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Company);

        var profile = snapshot.AcceptedProfile;
        var coverage = BuildCoverage(snapshot);
        var coveredTargets = coverage
            .Where(item => item.Value is CoverageLevel.Supported or CoverageLevel.Strong)
            .Select(item => item.Key)
            .ToHashSet();
        var missingTargets = ResearchTargetCatalog.All
            .Where(target => !coveredTargets.Contains(target))
            .ToArray();

        var baseStatus = DetermineBaseStatus(snapshot, profile is not null, coveredTargets.Count);
        var flags = new List<CompanyHealthStatus>();

        if (snapshot.IsArchived)
        {
            flags.Add(CompanyHealthStatus.Archived);
        }

        if (possibleDuplicate)
        {
            flags.Add(CompanyHealthStatus.PossibleDuplicate);
        }

        if (snapshot.PendingUpdate)
        {
            flags.Add(CompanyHealthStatus.PendingUpdate);
        }

        var now = asOf ?? DateTimeOffset.UtcNow;
        var isStale = profile is not null
            && snapshot.Company.LastResearchedAt is { } lastResearchedAt
            && lastResearchedAt <= now - options.StaleAfter;
        if (isStale)
        {
            flags.Add(CompanyHealthStatus.Stale);
        }

        var status = flags.FirstOrDefault() switch
        {
            CompanyHealthStatus.Archived => CompanyHealthStatus.Archived,
            CompanyHealthStatus.PossibleDuplicate => CompanyHealthStatus.PossibleDuplicate,
            CompanyHealthStatus.PendingUpdate => CompanyHealthStatus.PendingUpdate,
            CompanyHealthStatus.Stale => CompanyHealthStatus.Stale,
            _ => baseStatus
        };

        var reasons = BuildReasons(
            snapshot,
            baseStatus,
            status,
            coveredTargets.Count,
            missingTargets,
            flags,
            isStale);

        return new CompanyHealthAssessment(
            snapshot.Company.Id,
            status,
            baseStatus,
            flags,
            profile is not null,
            coveredTargets.Count,
            ResearchTargetCatalog.All.Count,
            Math.Max(0, snapshot.SourceDocumentCount),
            Math.Max(0, snapshot.ResearchRunCount),
            snapshot.Company.LastResearchedAt,
            missingTargets,
            reasons);
    }

    /// <summary>
    /// Convenience adapter for callers that already have a Company entity and
    /// want to keep the workspace boundary free of persistence concerns.
    /// </summary>
    public CompanyHealthAssessment Evaluate(
        Company company,
        CompanyProfileVersion? acceptedProfile = null,
        IReadOnlyCollection<EvidenceCoverageItem>? acceptedCoverage = null,
        int sourceDocumentCount = 0,
        int researchRunCount = 0,
        bool pendingUpdate = false,
        bool isArchived = false,
        bool possibleDuplicate = false,
        DateTimeOffset? asOf = null) =>
        Evaluate(
            CompanyWorkspaceSnapshot.FromCompany(
                company,
                acceptedProfile,
                acceptedCoverage,
                sourceDocumentCount,
                researchRunCount,
                pendingUpdate,
                isArchived),
            possibleDuplicate,
            asOf);

    private CompanyHealthStatus DetermineBaseStatus(
        CompanyWorkspaceSnapshot snapshot,
        bool hasAcceptedProfile,
        int coveredTargetCount)
    {
        if (!hasAcceptedProfile)
        {
            return snapshot.Company.LastResearchedAt is null
                && snapshot.SourceDocumentCount == 0
                && snapshot.ResearchRunCount == 0
                ? CompanyHealthStatus.Unresearched
                : CompanyHealthStatus.Sparse;
        }

        return coveredTargetCount >= ResearchTargetCatalog.All.Count
            ? CompanyHealthStatus.Complete
            : coveredTargetCount >= options.PartialMinimumCoveredTargets
                ? CompanyHealthStatus.Partial
                : CompanyHealthStatus.Sparse;
    }

    private static IReadOnlyDictionary<ResearchTarget, CoverageLevel> BuildCoverage(
        CompanyWorkspaceSnapshot snapshot)
    {
        var coverage = ResearchTargetCatalog.All.ToDictionary(
            target => target,
            _ => CoverageLevel.Missing);

        foreach (var item in snapshot.Coverage)
        {
            if (!coverage.ContainsKey(item.Target))
            {
                continue;
            }

            coverage[item.Target] = Max(coverage[item.Target], item.Level);
        }

        if (snapshot.AcceptedProfile is not { } profile)
        {
            return coverage;
        }

        foreach (var (target, populated) in ProfilePopulation(profile))
        {
            if (populated)
            {
                coverage[target] = Max(coverage[target], CoverageLevel.Supported);
            }
        }

        return coverage;
    }

    private static IEnumerable<KeyValuePair<ResearchTarget, bool>> ProfilePopulation(
        CompanyProfileVersion profile)
    {
        yield return new(ResearchTarget.LegalIdentity, !string.IsNullOrWhiteSpace(profile.LegalName));
        yield return new(ResearchTarget.TaxRegistration, !string.IsNullOrWhiteSpace(profile.RegistrationNumberOrTaxId));
        yield return new(ResearchTarget.FoundedHistory, profile.FoundedYear.HasValue);
        yield return new(
            ResearchTarget.Industry,
            !string.IsNullOrWhiteSpace(profile.PrimaryIndustry) || profile.SecondaryIndustries.Count > 0);
        yield return new(
            ResearchTarget.EmployeeScale,
            !string.IsNullOrWhiteSpace(profile.CompanySize)
            || profile.EmployeeCount.HasValue
            || !string.IsNullOrWhiteSpace(profile.EmployeeCountRange));
        yield return new(ResearchTarget.ProductsServices, profile.ProductsServices.Count > 0);
        yield return new(ResearchTarget.Markets, profile.Markets.Count > 0);
        yield return new(ResearchTarget.Leadership, profile.Leadership.Count > 0);
        yield return new(ResearchTarget.Locations, profile.Locations.Count > 0);
    }

    private static CoverageLevel Max(CoverageLevel left, CoverageLevel right) =>
        (CoverageLevel)Math.Max((int)left, (int)right);

    private static IReadOnlyList<string> BuildReasons(
        CompanyWorkspaceSnapshot snapshot,
        CompanyHealthStatus baseStatus,
        CompanyHealthStatus status,
        int coveredTargetCount,
        IReadOnlyList<ResearchTarget> missingTargets,
        IReadOnlyCollection<CompanyHealthStatus> flags,
        bool isStale)
    {
        var reasons = new List<string>();
        if (status == CompanyHealthStatus.Unresearched)
        {
            reasons.Add("No accepted profile or research evidence is recorded.");
        }
        else if (baseStatus == CompanyHealthStatus.Sparse)
        {
            reasons.Add("Accepted profile coverage is sparse or research has not produced an accepted profile.");
        }
        else if (baseStatus == CompanyHealthStatus.Partial)
        {
            reasons.Add($"Accepted profile covers {coveredTargetCount} of {ResearchTargetCatalog.All.Count} research targets.");
        }
        else if (baseStatus == CompanyHealthStatus.Complete)
        {
            reasons.Add("Accepted profile covers all defined research targets.");
        }

        if (missingTargets.Count > 0)
        {
            reasons.Add($"Missing targets: {string.Join(", ", missingTargets)}.");
        }

        if (isStale)
        {
            reasons.Add("Last research is older than the configured freshness window.");
        }

        if (snapshot.PendingUpdate)
        {
            reasons.Add("Monitoring or refresh has a pending update for review.");
        }

        if (flags.Contains(CompanyHealthStatus.PossibleDuplicate))
        {
            reasons.Add("Deterministic identity signals match another workspace record.");
        }

        if (snapshot.IsArchived)
        {
            reasons.Add("Company is archived and hidden from the normal workspace list.");
        }

        return reasons;
    }
}
