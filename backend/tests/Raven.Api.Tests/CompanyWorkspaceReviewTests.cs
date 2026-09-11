using Raven.Api.Features.Companies;
using Raven.Api.Features.Companies.Workspace;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Research.Coverage;

namespace Raven.Api.Tests;

public sealed class CompanyWorkspaceReviewTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Health_evaluator_distinguishes_unresearched_sparse_partial_complete_and_stale_profiles()
    {
        var evaluator = new CompanyHealthEvaluator();

        var unresearched = evaluator.Evaluate(Snapshot("Unresearched"), asOf: AsOf);
        Assert.Equal(CompanyHealthStatus.Unresearched, unresearched.Status);
        Assert.Equal(0, unresearched.CoveredTargetCount);

        var sparseProfile = ProfileSnapshot("Sparse", profile =>
        {
            profile.ProductsServices.Add(new ProfileProductService("Payments"));
        });
        var sparse = evaluator.Evaluate(sparseProfile, asOf: AsOf);
        Assert.Equal(CompanyHealthStatus.Sparse, sparse.Status);
        Assert.Contains(ResearchTarget.Leadership, sparse.MissingTargets);
        Assert.Equal(1, sparse.CoveredTargetCount);

        var partialProfile = ProfileSnapshot("Partial", profile =>
        {
            profile.LegalName = "Partial Company Limited";
            profile.PrimaryIndustry = "Technology";
            profile.Locations.Add(new ProfileLocation("Hanoi", Country: "Vietnam"));
        });
        var partial = evaluator.Evaluate(partialProfile, asOf: AsOf);
        Assert.Equal(CompanyHealthStatus.Partial, partial.Status);
        Assert.Equal(3, partial.CoveredTargetCount);

        var completeProfile = ProfileSnapshot("Complete", PopulateAllTargets);
        var complete = evaluator.Evaluate(completeProfile, asOf: AsOf);
        Assert.Equal(CompanyHealthStatus.Complete, complete.Status);
        Assert.Empty(complete.MissingTargets);

        var staleSnapshot = ProfileSnapshot("Stale", PopulateAllTargets);
        staleSnapshot.Company.LastResearchedAt = AsOf.AddDays(-91);
        var stale = evaluator.Evaluate(staleSnapshot, asOf: AsOf);
        Assert.Equal(CompanyHealthStatus.Stale, stale.Status);
        Assert.Equal(CompanyHealthStatus.Complete, stale.BaseStatus);
        Assert.Contains(CompanyHealthStatus.Stale, stale.Flags);
    }

    [Fact]
    public void Health_evaluator_preserves_overlay_flags_and_uses_safe_precedence()
    {
        var snapshot = ProfileSnapshot("Archived duplicate", PopulateAllTargets) with
        {
            IsArchived = true,
            PendingUpdate = true
        };

        var result = new CompanyHealthEvaluator().Evaluate(snapshot, possibleDuplicate: true, asOf: AsOf);

        Assert.Equal(CompanyHealthStatus.Archived, result.Status);
        Assert.Equal(CompanyHealthStatus.Complete, result.BaseStatus);
        Assert.Contains(CompanyHealthStatus.Archived, result.Flags);
        Assert.Contains(CompanyHealthStatus.PossibleDuplicate, result.Flags);
        Assert.Contains(CompanyHealthStatus.PendingUpdate, result.Flags);
    }

    [Fact]
    public void Duplicate_grouping_uses_exact_registration_website_legal_country_and_name_country_signals()
    {
        var registrationOne = Snapshot(
            "Registration One",
            registrationNumber: "010-123 456",
            country: "Vietnam");
        var registrationTwo = Snapshot(
            "Registration Two",
            registrationNumber: "010123456",
            country: "Singapore");

        var websiteOne = Snapshot("Website One", website: "https://www.same.example/about", country: "Vietnam");
        var websiteTwo = Snapshot("Website Two", website: "same.example/contact", country: "Canada");

        var legalOne = Snapshot(
            "Legal One",
            legalName: "Công ty Cổ phần Legal Example",
            country: "Vietnam");
        var legalTwo = Snapshot(
            "Legal Two",
            legalName: "CONG TY CO PHAN LEGAL EXAMPLE",
            country: "vietnam");

        var nameOne = Snapshot("Name Example", country: "Vietnam", legalName: "Name Example Alpha");
        var nameTwo = Snapshot("name-example", country: "VIETNAM", legalName: "Name Example Beta");
        var foreignName = Snapshot("Name Example", country: "Canada", legalName: "Name Example Canada");

        var groups = new CompanyDuplicateGroupingService().Group(
        [
            registrationOne,
            registrationTwo,
            websiteOne,
            websiteTwo,
            legalOne,
            legalTwo,
            nameOne,
            nameTwo,
            foreignName
        ]);

        Assert.Equal(4, groups.Count);
        Assert.Contains(groups, group =>
            group.StrongestMatch == DuplicateMatchType.RegistrationNumber
            && group.Members.Select(member => member.CompanyId).ToHashSet()
                .SetEquals([registrationOne.Company.Id, registrationTwo.Company.Id]));
        Assert.Contains(groups, group =>
            group.StrongestMatch == DuplicateMatchType.WebsiteHost
            && group.Members.Select(member => member.CompanyId).ToHashSet()
                .SetEquals([websiteOne.Company.Id, websiteTwo.Company.Id]));
        Assert.Contains(groups, group =>
            group.StrongestMatch == DuplicateMatchType.LegalNameAndCountry
            && group.Members.Select(member => member.CompanyId).ToHashSet()
                .SetEquals([legalOne.Company.Id, legalTwo.Company.Id]));
        Assert.Contains(groups, group =>
            group.StrongestMatch == DuplicateMatchType.NameAndCountry
            && group.Members.Select(member => member.CompanyId).ToHashSet()
                .SetEquals([nameOne.Company.Id, nameTwo.Company.Id]));
        Assert.DoesNotContain(groups, group => group.Members.Any(member => member.CompanyId == foreignName.Company.Id));
    }

    [Fact]
    public async Task Workspace_review_keeps_deterministic_findings_when_ai_is_unavailable()
    {
        var duplicateOne = Snapshot("FPT Software", website: "https://fptsoftware.com", country: "Vietnam");
        var duplicateTwo = Snapshot("FPT Software", website: "fptsoftware.com/about", country: "Vietnam");
        var sparse = ProfileSnapshot("Sparse Company", profile =>
        {
            profile.ProductsServices.Add(new ProfileProductService("One service"));
        });
        var archived = Snapshot("Archived", isArchived: true);

        var service = new CompanyWorkspaceReviewService(aiProvider: new ThrowingAiProvider());
        var result = await service.ReviewAsync(new WorkspaceReviewRequest(
            [duplicateOne, duplicateTwo, sparse, archived],
            IncludeArchived: false,
            AsOf: AsOf));

        Assert.Equal(3, result.Companies.Count);
        Assert.Single(result.DuplicateGroups);
        Assert.Contains(result.Recommendations, recommendation =>
            recommendation.Kind == WorkspaceReviewRecommendationKind.PossibleDuplicate);
        Assert.Contains(result.Recommendations, recommendation =>
            recommendation.Kind == WorkspaceReviewRecommendationKind.SparseProfile);
        Assert.False(result.AiUsed);
        Assert.Contains("deterministic", result.AiWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Workspace_review_accepts_ai_alias_suggestions_only_as_non_mutating_review_cards()
    {
        var company = Snapshot("FPT Information System", country: "Vietnam");
        var service = new CompanyWorkspaceReviewService(aiProvider: new StubAiProvider(company.Company.Id));

        var result = await service.ReviewAsync(new WorkspaceReviewRequest([company], IncludeAiSuggestions: true));

        var alias = Assert.Single(result.Recommendations, item => item.IsAiGenerated);
        Assert.Equal(WorkspaceReviewRecommendationKind.PossibleAlias, alias.Kind);
        Assert.Equal(company.Company.Id, alias.CompanyId);
        Assert.True(result.AiUsed);
        Assert.Empty(result.DuplicateGroups);
    }

    private static CompanyWorkspaceSnapshot Snapshot(
        string name,
        string? website = null,
        string? country = null,
        string? legalName = null,
        string? registrationNumber = null,
        bool isArchived = false,
        bool pendingUpdate = false)
    {
        var company = new Company
        {
            Name = name,
            Website = website,
            Country = country,
            LegalName = legalName,
            RegistrationNumber = registrationNumber
        };

        return new CompanyWorkspaceSnapshot(
            company,
            IsArchived: isArchived,
            PendingUpdate: pendingUpdate);
    }

    private static CompanyWorkspaceSnapshot ProfileSnapshot(
        string name,
        Action<CompanyProfileVersion> configure,
        string? country = "Vietnam")
    {
        var company = new Company
        {
            Name = name,
            Country = country
        };
        var profile = new CompanyProfileVersion
        {
            CompanyId = company.Id,
            ResearchRunId = Guid.NewGuid()
        };
        configure(profile);
        return new CompanyWorkspaceSnapshot(company, profile);
    }

    private static void PopulateAllTargets(CompanyProfileVersion profile)
    {
        profile.LegalName = "Complete Company Limited";
        profile.RegistrationNumberOrTaxId = "0101234567";
        profile.FoundedYear = 1999;
        profile.PrimaryIndustry = "Technology";
        profile.CompanySize = "Large";
        profile.ProductsServices.Add(new ProfileProductService("Platform"));
        profile.Markets.Add(new ProfileMarket("Vietnam"));
        profile.Leadership.Add(new ProfileLeader("A. Executive", "CEO"));
        profile.Locations.Add(new ProfileLocation("Hanoi", Country: "Vietnam"));
    }

    private sealed class ThrowingAiProvider : IWorkspaceReviewAiProvider
    {
        public Task<WorkspaceReviewAiResult> ReviewAsync(
            WorkspaceReviewAiRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("AI unavailable in this test.");
    }

    private sealed class StubAiProvider(Guid companyId) : IWorkspaceReviewAiProvider
    {
        public Task<WorkspaceReviewAiResult> ReviewAsync(
            WorkspaceReviewAiRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkspaceReviewAiResult(
            [
                new WorkspaceReviewAiSuggestion(
                    WorkspaceReviewRecommendationKind.PossibleAlias,
                    companyId,
                    "Possible alias",
                    "The name may be an alternate spelling for another workspace record.")
            ]));
    }
}
