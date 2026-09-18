using Raven.Api.Features.Research.Coverage;

namespace Raven.Api.Features.Profiles;

/// <summary>
/// Describes how much supported research a profile contains. A profile row is
/// not automatically a usable profile: a failed or stale workflow must not be
/// able to turn an identity-only snapshot into a Profile Improvement baseline.
/// </summary>
public enum CompanyProfileReadinessState
{
    IdentityOnly,
    Sparse,
    Partial,
    Complete
}

public static class CompanyProfileReadiness
{
    public const int RequiredTargetCount = 9;
    public const int PartialMinimumCoveredTargets = 3;

    public static CompanyProfileReadinessState GetState(CompanyProfileSnapshot profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var covered = CountCoveredTargets(profile);
        return covered switch
        {
            0 => CompanyProfileReadinessState.IdentityOnly,
            >= RequiredTargetCount => CompanyProfileReadinessState.Complete,
            >= PartialMinimumCoveredTargets => CompanyProfileReadinessState.Partial,
            _ => CompanyProfileReadinessState.Sparse
        };
    }

    public static int CountCoveredTargets(CompanyProfileSnapshot profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var count = 0;
        if (!string.IsNullOrWhiteSpace(profile.LegalName)) count++;
        if (!string.IsNullOrWhiteSpace(profile.RegistrationNumberOrTaxId)) count++;
        if (profile.FoundedYear.HasValue) count++;
        if (!string.IsNullOrWhiteSpace(profile.PrimaryIndustry) || profile.SecondaryIndustries.Count > 0) count++;
        if (!string.IsNullOrWhiteSpace(profile.CompanySize)
            || profile.EmployeeCount.HasValue
            || !string.IsNullOrWhiteSpace(profile.EmployeeCountRange)) count++;
        if (profile.ProductsServices.Count > 0) count++;
        if (profile.Markets.Count > 0) count++;
        if (profile.Leadership.Count > 0) count++;
        if (profile.Locations.Count > 0) count++;
        return count;
    }

    /// <summary>
    /// The generated profile provenance is deliberately separate from a
    /// company name. It lets the server distinguish a confirmed model result
    /// from an accidental identity-only row left by an interrupted workflow.
    /// </summary>
    public static bool HasModelCreationProvenance(CompanyProfileSnapshot profile) =>
        profile switch
        {
            CompanyProfileCandidate candidate => HasModelCreationProvenance(candidate.AiProvider, candidate.AiModel, candidate.PromptTemplateVersion),
            CompanyProfileVersion version => HasModelCreationProvenance(version.AiProvider, version.AiModel, version.PromptTemplateVersion),
            _ => false
        };

    public static bool IsUsableAcceptedProfile(CompanyProfileVersion profile) =>
        HasModelCreationProvenance(profile) && CountCoveredTargets(profile) > 0;

    public static bool IsConfirmableCandidate(CompanyProfileCandidate candidate) =>
        HasModelCreationProvenance(candidate)
        && CountCoveredTargets(candidate) > 0
        && candidate.Evidence.Any(evidence => evidence.SourceDocumentIds.Count > 0);

    private static bool HasModelCreationProvenance(string? provider, string? model, string? promptTemplateVersion) =>
        !string.IsNullOrWhiteSpace(provider)
        && !string.IsNullOrWhiteSpace(model)
        && !string.IsNullOrWhiteSpace(promptTemplateVersion);
}
