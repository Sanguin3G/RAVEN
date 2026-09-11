using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Settings;

namespace Raven.Api.Tests;

public sealed class ResearchSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_creates_and_persists_product_defaults()
    {
        var store = new InMemoryResearchSettingsStore();
        var service = new ResearchSettingsService(store);

        var settings = await service.GetAsync();
        var persisted = await store.GetAsync();

        Assert.Equal(GroundingMode.Auto, settings.GroundingMode);
        Assert.Equal("gemini-3.5-flash-lite", settings.ProfileModel);
        Assert.Equal("gemini-3.5-flash-lite", settings.GroundingModel);
        Assert.Equal("gemini-3.8-flash", settings.DeepResearchModel);
        Assert.True(settings.AiSourceRerankingEnabled);
        Assert.Equal(ProviderPreset.LocalFirst, settings.ProviderPreset);
        Assert.Equal(["brave"], settings.SearchProviderPriority);
        Assert.Equal(["crawl4ai-local"], settings.CrawlerProviderPriority);
        Assert.NotNull(persisted);
        Assert.Equal(ResearchSettingsEntity.SingletonKey, persisted!.Id);
    }

    [Fact]
    public async Task UpdateAsync_persists_values_for_a_new_service_instance()
    {
        var store = new InMemoryResearchSettingsStore();
        var firstService = new ResearchSettingsService(store);
        var expectedUpdate = new UpdateResearchSettingsRequest(
            GroundingMode.Always,
            "gemini-3.8-flash",
            "gemini-3.5-flash-lite",
            "gemini-3.5-flash-lite",
            false,
            ProviderPreset.Custom,
            ["exa", "brave", "EXA"],
            ["firecrawl", "crawl4ai-local"]);

        var updated = await firstService.UpdateAsync(expectedUpdate);
        var reloaded = await new ResearchSettingsService(store).GetAsync();

        Assert.Equal(expectedUpdate.GroundingMode, updated.GroundingMode);
        Assert.Equal(expectedUpdate.ProfileModel, reloaded.ProfileModel);
        Assert.Equal(expectedUpdate.GroundingModel, reloaded.GroundingModel);
        Assert.Equal(expectedUpdate.DeepResearchModel, reloaded.DeepResearchModel);
        Assert.False(reloaded.AiSourceRerankingEnabled);
        Assert.Equal(ProviderPreset.Custom, reloaded.ProviderPreset);
        Assert.Equal(["exa", "brave"], reloaded.SearchProviderPriority);
        Assert.Equal(["firecrawl", "crawl4ai-local"], reloaded.CrawlerProviderPriority);
    }

    [Fact]
    public async Task ResetAsync_restores_defaults_after_a_custom_update()
    {
        var store = new InMemoryResearchSettingsStore();
        var service = new ResearchSettingsService(store);

        await service.UpdateAsync(new UpdateResearchSettingsRequest(
            GroundingMode.Off,
            "gemini-3.8-flash",
            "gemini-3.8-flash",
            "gemini-3.5-flash-lite",
            false,
            ProviderPreset.Cloud,
            ["exa"],
            ["firecrawl"]));

        var reset = await service.ResetAsync();

        Assert.Equal(GroundingMode.Auto, reset.GroundingMode);
        Assert.Equal("gemini-3.5-flash-lite", reset.ProfileModel);
        Assert.Equal("gemini-3.5-flash-lite", reset.GroundingModel);
        Assert.Equal("gemini-3.8-flash", reset.DeepResearchModel);
        Assert.True(reset.AiSourceRerankingEnabled);
        Assert.Equal(ProviderPreset.LocalFirst, reset.ProviderPreset);
        Assert.Equal(["brave"], reset.SearchProviderPriority);
        Assert.Equal(["crawl4ai-local"], reset.CrawlerProviderPriority);
    }

    [Fact]
    public async Task Invalid_update_does_not_overwrite_previous_settings()
    {
        var store = new InMemoryResearchSettingsStore();
        var service = new ResearchSettingsService(store);
        var before = await service.GetAsync();

        var error = await Assert.ThrowsAsync<ResearchSettingsValidationException>(() => service.UpdateAsync(
            new UpdateResearchSettingsRequest(
                GroundingMode.Auto,
                "gemini-unknown",
                "gemini-3.5-flash-lite",
                "gemini-3.8-flash",
                false,
                ProviderPreset.LocalFirst,
                [""],
                ["crawl4ai-local"])));
        var after = await service.GetAsync();

        Assert.Contains("Profile model is not supported.", error.Errors);
        Assert.Contains("Search provider IDs cannot be empty.", error.Errors);
        Assert.Equal(before.ProfileModel, after.ProfileModel);
        Assert.Equal(before.AiSourceRerankingEnabled, after.AiSourceRerankingEnabled);
        Assert.Equal(before.SearchProviderPriority, after.SearchProviderPriority);
    }

    [Fact]
    public async Task GetAsync_repairs_an_invalid_persisted_row_to_defaults()
    {
        var store = new InMemoryResearchSettingsStore();
        await store.SaveAsync(new ResearchSettingsEntity
        {
            Id = ResearchSettingsEntity.SingletonKey,
            ProfileModel = "gemini-unavailable",
            GroundingModel = "gemini-3.5-flash-lite",
            DeepResearchModel = "gemini-3.8-flash",
            SearchProviderPriority = ["brave"],
            CrawlerProviderPriority = ["crawl4ai-local"]
        });

        var repaired = await new ResearchSettingsService(store).GetAsync();

        Assert.Equal("gemini-3.5-flash-lite", repaired.ProfileModel);
        Assert.Equal(ProviderPreset.LocalFirst, repaired.ProviderPreset);
        Assert.True(repaired.AiSourceRerankingEnabled);
    }
}
