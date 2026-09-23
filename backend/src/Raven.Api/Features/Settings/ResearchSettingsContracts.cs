using Raven.Api.Features.Research.Intelligence;

namespace Raven.Api.Features.Settings;

public enum ProviderPreset
{
    Resilient,
    LocalFirst,
    Cloud,
    Custom
}

public enum ManagedResearchDepth
{
    Adaptive,
    Focused,
    Standard,
    Thorough,
    Exhaustive
}

public sealed record ResearchSettingsResponse(
    GroundingMode GroundingMode,
    string ProfileModel,
    string GroundingModel,
    string DeepResearchModel,
    bool AiSourceRerankingEnabled,
    ProviderPreset ProviderPreset,
    IReadOnlyList<string> SearchProviderPriority,
    IReadOnlyList<string> CrawlerProviderPriority,
    DateTimeOffset UpdatedAt,
    string ManagedResearchProvider = ResearchSettingsDefaults.ManagedResearchProvider,
    ManagedResearchDepth ManagedResearchDepth = ManagedResearchDepth.Adaptive,
    IReadOnlyList<string>? CustomSearchProviderPriority = null,
    IReadOnlyList<string>? CustomCrawlerProviderPriority = null,
    string ChatModel = ResearchSettingsDefaults.ChatModel);

public sealed record UpdateResearchSettingsRequest(
    GroundingMode GroundingMode,
    string ProfileModel,
    string GroundingModel,
    string DeepResearchModel,
    bool AiSourceRerankingEnabled,
    ProviderPreset ProviderPreset,
    IReadOnlyList<string>? SearchProviderPriority = null,
    IReadOnlyList<string>? CrawlerProviderPriority = null,
    string? ManagedResearchProvider = null,
    ManagedResearchDepth? ManagedResearchDepth = null,
    IReadOnlyList<string>? CustomSearchProviderPriority = null,
    IReadOnlyList<string>? CustomCrawlerProviderPriority = null,
    string? ChatModel = null);

public interface IResearchSettingsService
{
    Task<ResearchSettingsResponse> GetAsync(CancellationToken cancellationToken = default);

    Task<ResearchSettingsResponse> UpdateAsync(
        UpdateResearchSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<ResearchSettingsResponse> ResetAsync(CancellationToken cancellationToken = default);
}
