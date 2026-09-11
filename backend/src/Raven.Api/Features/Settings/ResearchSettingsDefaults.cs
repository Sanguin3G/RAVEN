using Raven.Api.Features.Ai;
using Raven.Api.Features.Research.Intelligence;
using BraveProvider = Raven.Api.Features.Search.BraveSearchProvider;
using CrawlProvider = Raven.Api.Features.Crawling.Crawl4AiLocalProvider;
using ExaCrawlProvider = Raven.Api.Features.Crawling.Exa.ExaCrawlerProvider;

namespace Raven.Api.Features.Settings;

/// <summary>
/// Safe product defaults for research settings. These are identifiers only;
/// they never contain provider credentials.
/// </summary>
public static class ResearchSettingsDefaults
{
    public const string ProfileModel = RuntimeModelPreferences.FlashLite;
    public const string GroundingModel = RuntimeModelPreferences.FlashLite;
    public const string DeepResearchModel = RuntimeModelPreferences.Flash;

    public const string BraveSearchProvider = BraveProvider.ProviderId;
    public const string Crawl4AiLocalProvider = CrawlProvider.ProviderId;
    public const string ExaCrawlerProvider = ExaCrawlProvider.ProviderId;

    public static ResearchSettingsEntity CreateEntity(DateTimeOffset? updatedAt = null) => new()
    {
        Id = ResearchSettingsEntity.SingletonKey,
        GroundingMode = GroundingMode.Auto,
        ProfileModel = ProfileModel,
        GroundingModel = GroundingModel,
        DeepResearchModel = DeepResearchModel,
        AiSourceRerankingEnabled = true,
        ProviderPreset = ProviderPreset.LocalFirst,
        SearchProviderPriority = [BraveSearchProvider],
        CrawlerProviderPriority = [Crawl4AiLocalProvider],
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow
    };
}
