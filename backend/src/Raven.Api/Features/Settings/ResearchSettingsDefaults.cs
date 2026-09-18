using Raven.Api.Features.Ai;
using Raven.Api.Features.Research.Intelligence;
using BraveProvider = Raven.Api.Features.Search.BraveSearchProvider;
using ExaSearch = Raven.Api.Features.Search.Exa.ExaSearchProvider;
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
    public const string ExaSearchProvider = ExaSearch.ProviderId;
    public const string Crawl4AiLocalProvider = CrawlProvider.ProviderId;
    public const string ExaCrawlerProvider = ExaCrawlProvider.ProviderId;

    /// <summary>
    /// Provider identifiers that may be persisted for native search. Keep this
    /// list in the settings boundary so removed providers cannot be selected by
    /// a new settings update.
    /// </summary>
    public static IReadOnlySet<string> SupportedSearchProviders { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BraveSearchProvider,
            ExaSearchProvider
        };

    /// <summary>
    /// Provider identifiers that may be persisted for native acquisition.
    /// </summary>
    public static IReadOnlySet<string> SupportedCrawlerProviders { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Crawl4AiLocalProvider,
            ExaCrawlerProvider
        };

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
