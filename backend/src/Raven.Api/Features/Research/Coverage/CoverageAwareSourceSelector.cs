using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Features.Research.Coverage;

/// <summary>
/// The metadata needed to choose a research root. The semantic fields are
/// intentionally supplied by the caller so this selector stays deterministic
/// and does not make an AI call or depend on persistence.
/// </summary>
public sealed record CoverageSourceCandidate(
    Guid CandidateId,
    string Url,
    string? Title,
    string? Snippet,
    SourceKind SourceKind,
    int DeterministicScore,
    int SearchRank,
    EntityRelationship EntityRelationship = EntityRelationship.SameEntity,
    CandidateRelevance Relevance = CandidateRelevance.High,
    IReadOnlyList<string>? ExpectedPurposes = null,
    IReadOnlyList<string>? DeterministicReasons = null,
    bool SemanticRecommended = false,
    string? SemanticRationale = null,
    bool OfficialDomain = false,
    string? NormalizedUrl = null);

/// <summary>
/// A selected research root and the evidence objectives it contributes. One
/// root may expand into multiple SourceDocuments during bounded acquisition.
/// </summary>
public sealed record CoverageSourceRoot(
    CoverageSourceCandidate Candidate,
    IReadOnlyList<ResearchTarget> CoveredTargets,
    IReadOnlyList<ResearchTarget> NewlyCoveredTargets,
    int SelectionScore,
    IReadOnlyList<string> Reasons);

public sealed record CoverageSourceSelectionRequest(
    IReadOnlyCollection<CoverageSourceCandidate> Candidates,
    IReadOnlyCollection<ResearchTarget>? Targets = null,
    int MaximumRoots = 5);

public sealed record CoverageSourceSelectionResult(
    IReadOnlyList<CoverageSourceRoot> RecommendedRoots);

/// <summary>
/// Selects up to five complementary, high-value research roots. Selection is
/// greedy by marginal target coverage, with source authority and quality used
/// as tie-breakers. It deliberately stops when no eligible candidate adds new
/// coverage; five is a maximum, not a quota.
/// </summary>
public sealed class CoverageAwareSourceSelector
{
    private const int MaximumRecommendedRoots = 5;
    private const int NewTargetWeight = 100;
    private const int SourceDiversityWeight = 12;
    private const int DomainDiversityWeight = 5;
    private const int RedundantTargetPenalty = 24;
    private const int RelatedEntityPenalty = 45;

    private readonly ISourceAuthorityPolicy authorityPolicy;

    public CoverageAwareSourceSelector(ISourceAuthorityPolicy? authorityPolicy = null)
    {
        this.authorityPolicy = authorityPolicy ?? new SourceAuthorityPolicy();
    }

    public CoverageSourceSelectionResult Select(CoverageSourceSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestedTargets = NormalizeTargets(request.Targets);
        var maximumRoots = Math.Clamp(request.MaximumRoots, 0, MaximumRecommendedRoots);
        if (maximumRoots == 0 || request.Candidates.Count == 0)
        {
            return new CoverageSourceSelectionResult([]);
        }

        var candidates = request.Candidates
            .Where(candidate => IsEligible(candidate, requestedTargets))
            .Select(candidate => new PreparedCandidate(
                candidate,
                ResolvePurposes(candidate, requestedTargets)))
            .Where(candidate => candidate.Targets.Count > 0)
            .GroupBy(candidate => NormalizeIdentity(candidate.Candidate.NormalizedUrl ?? candidate.Candidate.Url), StringComparer.Ordinal)
            .Select(KeepBestDuplicate)
            .ToArray();

        var selected = new List<CoverageSourceRoot>(maximumRoots);
        var coveredTargets = new HashSet<ResearchTarget>();
        var selectedKinds = new HashSet<SourceKind>();
        var selectedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = candidates.ToList();

        while (selected.Count < maximumRoots)
        {
            var next = remaining
                .Select(candidate => Score(candidate, coveredTargets, selectedKinds, selectedDomains, requestedTargets))
                .Where(scored => scored.NewTargets.Count > 0)
                .OrderByDescending(scored => scored.Score)
                .ThenByDescending(scored => scored.NewTargets.Count)
                .ThenBy(scored => scored.Candidate.Candidate.SearchRank)
                .ThenBy(scored => NormalizeIdentity(scored.Candidate.Candidate.NormalizedUrl ?? scored.Candidate.Candidate.Url), StringComparer.Ordinal)
                .FirstOrDefault();

            if (next is null)
            {
                break;
            }

            var root = new CoverageSourceRoot(
                next.Candidate.Candidate,
                next.Candidate.Targets.OrderBy(target => target).ToArray(),
                next.NewTargets.OrderBy(target => target).ToArray(),
                next.Score,
                BuildReasons(next, coveredTargets.Count > 0));
            selected.Add(root);

            foreach (var target in next.Candidate.Targets)
            {
                coveredTargets.Add(target);
            }

            selectedKinds.Add(next.Candidate.Candidate.SourceKind);
            selectedDomains.Add(GetDomain(next.Candidate.Candidate));
            remaining.RemoveAll(candidate => candidate.Candidate.CandidateId == next.Candidate.Candidate.CandidateId);
        }

        return new CoverageSourceSelectionResult(selected);
    }

    private bool IsEligible(CoverageSourceCandidate candidate, IReadOnlySet<ResearchTarget> requestedTargets)
    {
        if (candidate.EntityRelationship is EntityRelationship.DifferentEntity or EntityRelationship.Uncertain ||
            candidate.Relevance == CandidateRelevance.Low ||
            candidate.SourceKind == SourceKind.SearchResult)
        {
            return false;
        }

        var purposes = ResolvePurposes(candidate, requestedTargets);
        return purposes.Count > 0;
    }

    private IReadOnlyList<ResearchTarget> ResolvePurposes(
        CoverageSourceCandidate candidate,
        IReadOnlySet<ResearchTarget> requestedTargets)
    {
        var parsed = ParsePurposes(candidate.ExpectedPurposes);
        if (parsed.Count == 0)
        {
            parsed = InferPurposes(candidate);
        }

        if (requestedTargets.Count > 0)
        {
            parsed.IntersectWith(requestedTargets);
        }

        return parsed.OrderBy(target => target).ToArray();
    }

    private PreparedCandidate KeepBestDuplicate(IEnumerable<PreparedCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => BaseQuality(candidate))
            .ThenByDescending(candidate => candidate.Candidate.DeterministicScore)
            .ThenBy(candidate => candidate.Candidate.SearchRank)
            .ThenBy(candidate => candidate.Candidate.CandidateId)
            .First();
    }

    private ScoredCandidate Score(
        PreparedCandidate candidate,
        IReadOnlySet<ResearchTarget> coveredTargets,
        IReadOnlySet<SourceKind> selectedKinds,
        IReadOnlySet<string> selectedDomains,
        IReadOnlySet<ResearchTarget> requestedTargets)
    {
        var newTargets = candidate.Targets.Where(target => !coveredTargets.Contains(target)).ToHashSet();
        var redundantTargets = candidate.Targets.Count - newTargets.Count;
        var sourceKindBonus = selectedKinds.Contains(candidate.Candidate.SourceKind) ? 0 : SourceDiversityWeight;
        var domain = GetDomain(candidate.Candidate);
        var domainBonus = selectedDomains.Contains(domain) ? 0 : DomainDiversityWeight;
        var relatedPenalty = candidate.Candidate.EntityRelationship == EntityRelationship.SameEntity
            ? 0
            : RelatedEntityPenalty;
        var score = BaseQuality(candidate)
            + (newTargets.Count * NewTargetWeight)
            + sourceKindBonus
            + domainBonus
            - (redundantTargets * RedundantTargetPenalty)
            - relatedPenalty;

        // A candidate that only repeats an already covered purpose is never a
        // recommendation. The selector recommends roots, not corroborating
        // copies of the same About page; later acquisition may still retain
        // those candidates for human review.
        return new ScoredCandidate(candidate, newTargets, score);
    }

    private int BaseQuality(PreparedCandidate candidate)
    {
        var sourceAuthority = candidate.Targets
            .Select(target => GetAuthorityRank(target, candidate.Candidate.SourceKind))
            .DefaultIfEmpty(0)
            .Average();
        var relevance = candidate.Candidate.Relevance switch
        {
            CandidateRelevance.High => 42,
            CandidateRelevance.Medium => 20,
            _ => -100
        };
        var relationship = candidate.Candidate.EntityRelationship switch
        {
            EntityRelationship.SameEntity => 45,
            EntityRelationship.Parent or EntityRelationship.Subsidiary or EntityRelationship.Affiliate => 8,
            _ => -100
        };
        var deterministic = Math.Clamp(candidate.Candidate.DeterministicScore / 25, -20, 50);
        var deterministicSignal = Math.Min(candidate.Candidate.DeterministicReasons?.Count ?? 0, 5) * 2;
        var semanticRecommendation = candidate.Candidate.SemanticRecommended ? 8 : 0;
        var officialDomain = candidate.Candidate.OfficialDomain ? 15 : 0;

        return (int)Math.Round(sourceAuthority / 2, MidpointRounding.AwayFromZero)
            + relevance
            + relationship
            + deterministic
            + deterministicSignal
            + semanticRecommendation
            + officialDomain;
    }

    private int GetAuthorityRank(ResearchTarget target, SourceKind sourceKind)
    {
        var field = target switch
        {
            ResearchTarget.LegalIdentity => SourceField.LegalIdentity,
            ResearchTarget.TaxRegistration => SourceField.TaxRegistration,
            ResearchTarget.ProductsServices => SourceField.ProductsServices,
            ResearchTarget.EmployeeScale => SourceField.EmployeeScale,
            ResearchTarget.Leadership => SourceField.Leadership,
            ResearchTarget.Locations => SourceField.OperatingLocations,
            _ => (SourceField?)null
        };

        if (field.HasValue)
        {
            return authorityPolicy.GetRank(field.Value, sourceKind);
        }

        return sourceKind switch
        {
            SourceKind.OfficialWebsite => 100,
            SourceKind.OfficialDocument => 95,
            SourceKind.OfficialBusinessRegistry => 90,
            SourceKind.BusinessDirectory => 78,
            SourceKind.TopCv => 70,
            SourceKind.LinkedIn => 65,
            SourceKind.News => 55,
            SourceKind.ExternalWebsite => 45,
            SourceKind.BusinessRegistry => 40,
            _ => 10
        };
    }

    private static IReadOnlySet<ResearchTarget> NormalizeTargets(IReadOnlyCollection<ResearchTarget>? targets) =>
        targets is null
            ? new HashSet<ResearchTarget>()
            : targets.ToHashSet();

    private static HashSet<ResearchTarget> ParsePurposes(IReadOnlyList<string>? purposes)
    {
        var parsed = new HashSet<ResearchTarget>();
        if (purposes is null)
        {
            return parsed;
        }

        foreach (var purpose in purposes)
        {
            var normalized = NormalizePurpose(purpose);
            if (normalized is null)
            {
                continue;
            }

            parsed.Add(normalized.Value);
        }

        return parsed;
    }

    private static ResearchTarget? NormalizePurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            return null;
        }

        var value = new string(purpose.Trim()
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToLowerInvariant();

        return value switch
        {
            "legalidentity" or "identity" or "legal" or "companyprofile" => ResearchTarget.LegalIdentity,
            "taxregistration" or "taxid" or "taxcode" or "registration" => ResearchTarget.TaxRegistration,
            "foundedhistory" or "history" or "founded" => ResearchTarget.FoundedHistory,
            "industry" or "businessactivities" or "registeredbusinessactivities" => ResearchTarget.Industry,
            "employeescale" or "companyscale" or "workforce" or "employees" => ResearchTarget.EmployeeScale,
            "productsservices" or "products" or "services" or "solutions" => ResearchTarget.ProductsServices,
            "markets" or "customers" or "global" => ResearchTarget.Markets,
            "leadership" or "management" or "executives" => ResearchTarget.Leadership,
            "locations" or "offices" or "operatinglocations" or "address" => ResearchTarget.Locations,
            _ => Enum.TryParse<ResearchTarget>(purpose.Trim(), true, out var target) ? target : null
        };
    }

    private static HashSet<ResearchTarget> InferPurposes(CoverageSourceCandidate candidate)
    {
        var text = string.Join(' ', candidate.Title, candidate.Url, candidate.Snippet).ToLowerInvariant();
        var inferred = new HashSet<ResearchTarget>();

        if (candidate.SourceKind is SourceKind.OfficialBusinessRegistry or SourceKind.BusinessDirectory or SourceKind.BusinessRegistry)
        {
            inferred.UnionWith([ResearchTarget.LegalIdentity, ResearchTarget.TaxRegistration, ResearchTarget.Industry]);
        }

        if (candidate.SourceKind is SourceKind.TopCv or SourceKind.LinkedIn)
        {
            inferred.Add(ResearchTarget.EmployeeScale);
        }

        if (candidate.SourceKind == SourceKind.OfficialDocument)
        {
            inferred.Add(ResearchTarget.FoundedHistory);
        }

        AddIfContains(text, inferred, ResearchTarget.FoundedHistory, "about", "company", "history", "profile");
        AddIfContains(text, inferred, ResearchTarget.ProductsServices, "product", "service", "solution", "platform");
        AddIfContains(text, inferred, ResearchTarget.Markets, "market", "customer", "global", "industry");
        AddIfContains(text, inferred, ResearchTarget.Leadership, "leadership", "management", "board", "executive", "ceo");
        AddIfContains(text, inferred, ResearchTarget.Locations, "location", "office", "contact", "headquarters");

        return inferred;
    }

    private static void AddIfContains(
        string text,
        ISet<ResearchTarget> targets,
        ResearchTarget target,
        params string[] signals)
    {
        if (signals.Any(text.Contains))
        {
            targets.Add(target);
        }
    }

    private static string NormalizeIdentity(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url.Trim().ToLowerInvariant();
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };
        return builder.Uri.ToString().TrimEnd('/').ToLowerInvariant();
    }

    private static string GetDomain(CoverageSourceCandidate candidate)
    {
        if (Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri))
        {
            return uri.Host.ToLowerInvariant();
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> BuildReasons(ScoredCandidate selected, bool hasPriorSelection)
    {
        var reasons = new List<string>
        {
            $"Covers: {string.Join(", ", selected.Candidate.Targets.OrderBy(target => target).Select(target => target.ToString()))}"
        };

        if (selected.NewTargets.Count > 0)
        {
            reasons.Add($"Adds coverage for: {string.Join(", ", selected.NewTargets.OrderBy(target => target).Select(target => target.ToString()))}");
        }

        reasons.Add(selected.Candidate.Candidate.EntityRelationship == EntityRelationship.SameEntity
            ? "Same company"
            : $"Related entity context: {selected.Candidate.Candidate.EntityRelationship}");
        reasons.Add($"Semantic relevance: {selected.Candidate.Candidate.Relevance}");
        reasons.Add($"Source kind: {selected.Candidate.Candidate.SourceKind}");

        if (selected.Candidate.Candidate.OfficialDomain)
        {
            reasons.Add("Official domain");
        }

        if (hasPriorSelection)
        {
            reasons.Add("Complementary root for uncovered dossier evidence");
        }

        if (selected.Candidate.Candidate.DeterministicReasons is { Count: > 0 } deterministicReasons)
        {
            reasons.Add($"Discovery signals: {string.Join(", ", deterministicReasons.Take(3))}");
        }

        if (!string.IsNullOrWhiteSpace(selected.Candidate.Candidate.SemanticRationale))
        {
            reasons.Add($"Rationale: {selected.Candidate.Candidate.SemanticRationale.Trim()}");
        }

        return reasons;
    }

    private sealed record PreparedCandidate(
        CoverageSourceCandidate Candidate,
        IReadOnlyList<ResearchTarget> Targets);

    private sealed record ScoredCandidate(
        PreparedCandidate Candidate,
        IReadOnlySet<ResearchTarget> NewTargets,
        int Score);
}
