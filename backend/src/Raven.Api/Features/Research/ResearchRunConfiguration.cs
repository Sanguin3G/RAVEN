using Raven.Api.Features.Settings;
using Raven.Api.Features.Research.Intelligence;

namespace Raven.Api.Features.Research;

/// <summary>Immutable provider/model choices resolved once for a research scope.</summary>
public sealed record ResearchRunConfiguration(
    GroundingMode GroundingMode,
    string ProfileModel,
    string IdentityModel,
    string DeepResearchModel,
    bool AiSourceRerankingEnabled,
    ProviderPreset ProviderPreset,
    IReadOnlyList<string> SearchProviderPriority,
    IReadOnlyList<string> CrawlerProviderPriority)
{
    public static ResearchRunConfiguration From(ResearchSettingsResponse settings) => new(
        settings.GroundingMode, settings.ProfileModel, settings.GroundingModel, settings.DeepResearchModel,
        settings.AiSourceRerankingEnabled, settings.ProviderPreset,
        settings.SearchProviderPriority.ToArray(), settings.CrawlerProviderPriority.ToArray());
}

public interface IResearchRunConfigurationSnapshot
{
    ResearchRunConfiguration? Current { get; }
    void Set(ResearchSettingsResponse settings);
    Task<ResearchRunConfiguration> GetOrLoadAsync(IResearchSettingsService settings, CancellationToken cancellationToken);
}

public sealed class ResearchRunConfigurationSnapshot : IResearchRunConfigurationSnapshot
{
    public ResearchRunConfiguration? Current { get; private set; }
    public void Set(ResearchSettingsResponse settings) => Current = ResearchRunConfiguration.From(settings);
    public async Task<ResearchRunConfiguration> GetOrLoadAsync(IResearchSettingsService settings, CancellationToken cancellationToken)
    {
        if (Current is not null) return Current;
        var resolved = await settings.GetAsync(cancellationToken);
        Set(resolved);
        return Current!;
    }
}
