using Raven.Api.Features.Research.Intelligence;

namespace Raven.Api.Features.Settings;

/// <summary>
/// Persistence-shaped representation of the application-wide research settings.
///
/// The row is intentionally a singleton today. A future EF/SQLite adapter should
/// map the two provider-priority lists to JSON columns (or a small owned table)
/// and enforce <see cref="SingletonKey"/> as the stable key. Secrets are not part
/// of this record; provider credentials remain in environment/user-secret config.
/// </summary>
public sealed class ResearchSettingsEntity
{
    public const string SingletonKey = "research";

    public string Id { get; set; } = SingletonKey;

    public GroundingMode GroundingMode { get; set; } = GroundingMode.Auto;

    public string ProfileModel { get; set; } = ResearchSettingsDefaults.ProfileModel;

    public string GroundingModel { get; set; } = ResearchSettingsDefaults.GroundingModel;

    public string DeepResearchModel { get; set; } = ResearchSettingsDefaults.DeepResearchModel;

    public string ChatModel { get; set; } = ResearchSettingsDefaults.ChatModel;

    public string ManagedResearchProvider { get; set; } = ResearchSettingsDefaults.ManagedResearchProvider;

    public ManagedResearchDepth ManagedResearchDepth { get; set; } = ManagedResearchDepth.Adaptive;

    public bool AiSourceRerankingEnabled { get; set; } = true;

    public ProviderPreset ProviderPreset { get; set; } = ProviderPreset.LocalFirst;

    public List<string> SearchProviderPriority { get; set; } =
        [ResearchSettingsDefaults.BraveSearchProvider];

    public List<string> CrawlerProviderPriority { get; set; } =
        [ResearchSettingsDefaults.Crawl4AiLocalProvider];

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ResearchSettingsEntity Clone() => new()
    {
        Id = Id,
        GroundingMode = GroundingMode,
        ProfileModel = ProfileModel,
        GroundingModel = GroundingModel,
        DeepResearchModel = DeepResearchModel,
        ChatModel = ChatModel,
        ManagedResearchProvider = ManagedResearchProvider,
        ManagedResearchDepth = ManagedResearchDepth,
        AiSourceRerankingEnabled = AiSourceRerankingEnabled,
        ProviderPreset = ProviderPreset,
        SearchProviderPriority = [.. SearchProviderPriority],
        CrawlerProviderPriority = [.. CrawlerProviderPriority],
        UpdatedAt = UpdatedAt
    };
}
