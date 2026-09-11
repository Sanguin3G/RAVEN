using Raven.Api.Features.Research.Intelligence;

namespace Raven.Api.Features.Settings;

public enum ProviderPreset
{
    Balanced,
    LocalFirst,
    Cloud,
    Custom
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
    DateTimeOffset UpdatedAt);

public sealed record UpdateResearchSettingsRequest(
    GroundingMode GroundingMode,
    string ProfileModel,
    string GroundingModel,
    string DeepResearchModel,
    bool AiSourceRerankingEnabled,
    ProviderPreset ProviderPreset,
    IReadOnlyList<string>? SearchProviderPriority = null,
    IReadOnlyList<string>? CrawlerProviderPriority = null);

public interface IResearchSettingsService
{
    Task<ResearchSettingsResponse> GetAsync(CancellationToken cancellationToken = default);

    Task<ResearchSettingsResponse> UpdateAsync(
        UpdateResearchSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<ResearchSettingsResponse> ResetAsync(CancellationToken cancellationToken = default);
}
