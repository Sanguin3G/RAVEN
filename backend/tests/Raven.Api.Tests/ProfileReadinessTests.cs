using Raven.Api.Features.Profiles;

namespace Raven.Api.Tests;

public sealed class ProfileReadinessTests
{
    [Fact]
    public void Identity_only_profile_is_not_an_improvement_baseline()
    {
        var profile = new CompanyProfileVersion
        {
            DisplayName = "Interrupted Company",
            AiProvider = "gemini",
            AiModel = "profile-model",
            PromptTemplateVersion = "company-profile-v1"
        };

        Assert.Equal(CompanyProfileReadinessState.IdentityOnly, CompanyProfileReadiness.GetState(profile));
        Assert.False(CompanyProfileReadiness.IsUsableAcceptedProfile(profile));
    }

    [Fact]
    public void Model_created_sparse_profile_with_supported_evidence_can_be_improved()
    {
        var profile = new CompanyProfileVersion
        {
            DisplayName = "Sparse Company",
            PrimaryIndustry = "Technology",
            AiProvider = "gemini",
            AiModel = "profile-model",
            PromptTemplateVersion = "company-profile-v1"
        };

        Assert.Equal(CompanyProfileReadinessState.Sparse, CompanyProfileReadiness.GetState(profile));
        Assert.True(CompanyProfileReadiness.IsUsableAcceptedProfile(profile));
    }

    [Fact]
    public void Candidate_requires_model_provenance_supported_target_and_evidence_before_confirmation()
    {
        var candidate = new CompanyProfileCandidate
        {
            DisplayName = "Candidate Company",
            PrimaryIndustry = "Technology",
            AiProvider = "gemini",
            AiModel = "profile-model",
            PromptTemplateVersion = "company-profile-v1"
        };

        Assert.False(CompanyProfileReadiness.IsConfirmableCandidate(candidate));

        candidate.Evidence.Add(new ProfileEvidence
        {
            FieldPath = "primaryIndustry",
            SourceDocumentIds = { Guid.NewGuid() }
        });

        Assert.True(CompanyProfileReadiness.IsConfirmableCandidate(candidate));
    }
}
