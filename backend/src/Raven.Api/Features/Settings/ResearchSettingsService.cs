using Raven.Api.Features.Research.Intelligence;

namespace Raven.Api.Features.Settings;

/// <summary>
/// Reads, validates and persists the application-wide research settings.
/// Workflow code should depend on <see cref="IResearchSettingsService"/> rather
/// than reading configuration directly.
/// </summary>
public sealed class ResearchSettingsService(IResearchSettingsStore store) : IResearchSettingsService
{
    private static readonly HashSet<string> AllowedModels =
    [
        ResearchSettingsDefaults.ProfileModel,
        ResearchSettingsDefaults.DeepResearchModel
    ];

    private static readonly HashSet<string> AllowedManagedResearchProviders =
    [
        ResearchSettingsDefaults.ManagedResearchProvider
    ];

    public async Task<ResearchSettingsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var persisted = await store.GetAsync(cancellationToken);

        if (persisted is null)
        {
            var defaults = ResearchSettingsDefaults.CreateEntity();
            await store.SaveAsync(defaults, cancellationToken);
            return ToResponse(defaults);
        }

        // Provider identifiers are persisted for compatibility with older
        // workspaces. A removed provider (notably Firecrawl) must not make the
        // settings row unusable or cause routing to fail with an empty route.
        // Preserve every still-supported preference and use deterministic
        // product defaults only when a priority list has no usable entries.
        var normalized = NormalizeLegacyProviderPriorities(persisted);
        var validationErrors = ValidateEntity(normalized);
        if (validationErrors.Count == 0 && normalized.Id == ResearchSettingsEntity.SingletonKey)
        {
            if (!ProviderPrioritiesEqual(persisted, normalized))
            {
                await store.SaveAsync(normalized, cancellationToken);
            }

            return ToResponse(normalized);
        }

        // A manually edited/partially migrated row must not cause a research run
        // to operate with unsafe or unsupported settings. Repair it atomically by
        // restoring the known product defaults.
        var repaired = ResearchSettingsDefaults.CreateEntity();
        await store.SaveAsync(repaired, cancellationToken);
        return ToResponse(repaired);
    }

    public async Task<ResearchSettingsResponse> UpdateAsync(
        UpdateResearchSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = await GetEntityAsync(cancellationToken);
        var errors = ValidateRequest(request);
        if (errors.Count > 0)
        {
            throw new ResearchSettingsValidationException(errors);
        }

        var updated = new ResearchSettingsEntity
        {
            Id = ResearchSettingsEntity.SingletonKey,
            GroundingMode = request.GroundingMode,
            ProfileModel = NormalizeModel(request.ProfileModel),
            GroundingModel = NormalizeModel(request.GroundingModel),
            DeepResearchModel = NormalizeModel(request.DeepResearchModel),
            ManagedResearchProvider = NormalizeManagedResearchProvider(request.ManagedResearchProvider ?? current.ManagedResearchProvider),
            ManagedResearchDepth = request.ManagedResearchDepth ?? current.ManagedResearchDepth,
            AiSourceRerankingEnabled = request.AiSourceRerankingEnabled,
            ProviderPreset = request.ProviderPreset,
            SearchProviderPriority = NormalizeProviderIds(
                request.SearchProviderPriority ?? current.SearchProviderPriority,
                ResearchSettingsDefaults.SupportedSearchProviders,
                [ResearchSettingsDefaults.BraveSearchProvider]),
            CrawlerProviderPriority = NormalizeProviderIds(
                request.CrawlerProviderPriority ?? current.CrawlerProviderPriority,
                ResearchSettingsDefaults.SupportedCrawlerProviders,
                [ResearchSettingsDefaults.Crawl4AiLocalProvider]),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await store.SaveAsync(updated, cancellationToken);
        return ToResponse(updated);
    }

    /// <summary>
    /// Restores product defaults. The public contract intentionally stays
    /// additive: API wiring may expose this as a reset action later without
    /// changing the existing get/update DTOs.
    /// </summary>
    public async Task<ResearchSettingsResponse> ResetAsync(CancellationToken cancellationToken = default)
    {
        var defaults = ResearchSettingsDefaults.CreateEntity();
        await store.SaveAsync(defaults, cancellationToken);
        return ToResponse(defaults);
    }

    private async Task<ResearchSettingsEntity> GetEntityAsync(CancellationToken cancellationToken)
    {
        var response = await GetAsync(cancellationToken);
        return new ResearchSettingsEntity
        {
            Id = ResearchSettingsEntity.SingletonKey,
            GroundingMode = response.GroundingMode,
            ProfileModel = response.ProfileModel,
            GroundingModel = response.GroundingModel,
            DeepResearchModel = response.DeepResearchModel,
            ManagedResearchProvider = response.ManagedResearchProvider,
            ManagedResearchDepth = response.ManagedResearchDepth,
            AiSourceRerankingEnabled = response.AiSourceRerankingEnabled,
            ProviderPreset = response.ProviderPreset,
            SearchProviderPriority = [.. response.SearchProviderPriority],
            CrawlerProviderPriority = [.. response.CrawlerProviderPriority],
            UpdatedAt = response.UpdatedAt
        };
    }

    private static List<string> ValidateRequest(UpdateResearchSettingsRequest request)
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(request.GroundingMode))
        {
            errors.Add("Grounding mode is not supported.");
        }

        if (!AllowedModels.Contains(request.ProfileModel?.Trim() ?? string.Empty))
        {
            errors.Add("Profile model is not supported.");
        }

        if (!AllowedModels.Contains(request.GroundingModel?.Trim() ?? string.Empty))
        {
            errors.Add("Grounding model is not supported.");
        }

        if (!AllowedModels.Contains(request.DeepResearchModel?.Trim() ?? string.Empty))
        {
            errors.Add("Deep Research model is not supported.");
        }

        if (!AllowedManagedResearchProviders.Contains(request.ManagedResearchProvider?.Trim() ?? ResearchSettingsDefaults.ManagedResearchProvider))
        {
            errors.Add("Managed AI research provider is not supported.");
        }

        if (request.ManagedResearchDepth is not null && !Enum.IsDefined(request.ManagedResearchDepth.Value))
        {
            errors.Add("Managed AI research depth is not supported.");
        }

        if (!Enum.IsDefined(request.ProviderPreset))
        {
            errors.Add("Provider preset is not supported.");
        }

        AddProviderErrors(
            errors,
            request.SearchProviderPriority,
            "Search",
            ResearchSettingsDefaults.SupportedSearchProviders);
        AddProviderErrors(
            errors,
            request.CrawlerProviderPriority,
            "Crawler",
            ResearchSettingsDefaults.SupportedCrawlerProviders);

        return errors;
    }

    private static List<string> ValidateEntity(ResearchSettingsEntity settings)
    {
        var errors = ValidateRequest(new UpdateResearchSettingsRequest(
            settings.GroundingMode,
            settings.ProfileModel,
            settings.GroundingModel,
            settings.DeepResearchModel,
            settings.AiSourceRerankingEnabled,
            settings.ProviderPreset,
            settings.SearchProviderPriority,
            settings.CrawlerProviderPriority,
            settings.ManagedResearchProvider,
            settings.ManagedResearchDepth));

        if (string.IsNullOrWhiteSpace(settings.Id))
        {
            errors.Add("Settings identity is missing.");
        }

        return errors;
    }

    private static void AddProviderErrors(
        ICollection<string> errors,
        IReadOnlyList<string>? providerIds,
        string category,
        IReadOnlySet<string> supportedProviders)
    {
        if (providerIds is null || providerIds.Count == 0)
        {
            errors.Add($"At least one {category.ToLowerInvariant()} provider is required.");
            return;
        }

        if (providerIds.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add($"{category} provider IDs cannot be empty.");
        }

        foreach (var providerId in providerIds.Where(providerId => !string.IsNullOrWhiteSpace(providerId)))
        {
            var normalizedProviderId = providerId.Trim();
            if (!supportedProviders.Contains(normalizedProviderId))
            {
                errors.Add($"{category} provider '{normalizedProviderId}' is not supported.");
            }
        }
    }

    private static string NormalizeModel(string model) => model.Trim();

    private static string NormalizeManagedResearchProvider(string provider) =>
        provider.Trim().ToLowerInvariant();

    private static List<string> NormalizeProviderIds(
        IReadOnlyList<string>? providerIds,
        IReadOnlySet<string> supportedProviders,
        IReadOnlyList<string> fallback) =>
        providerIds
            ?.Where(providerId => !string.IsNullOrWhiteSpace(providerId))
            .Select(providerId => providerId.Trim())
            .Where(supportedProviders.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [.. fallback];

    private static ResearchSettingsEntity NormalizeLegacyProviderPriorities(ResearchSettingsEntity persisted)
    {
        var normalized = persisted.Clone();
        normalized.SearchProviderPriority = NormalizeProviderIds(
            persisted.SearchProviderPriority,
            ResearchSettingsDefaults.SupportedSearchProviders,
            [ResearchSettingsDefaults.BraveSearchProvider]);
        normalized.CrawlerProviderPriority = NormalizeProviderIds(
            persisted.CrawlerProviderPriority,
            ResearchSettingsDefaults.SupportedCrawlerProviders,
            [ResearchSettingsDefaults.Crawl4AiLocalProvider]);
        if (string.IsNullOrWhiteSpace(normalized.ManagedResearchProvider))
        {
            normalized.ManagedResearchProvider = ResearchSettingsDefaults.ManagedResearchProvider;
        }

        if (!Enum.IsDefined(normalized.ManagedResearchDepth))
        {
            normalized.ManagedResearchDepth = ManagedResearchDepth.Adaptive;
        }

        return normalized;
    }

    private static bool ProviderPrioritiesEqual(
        ResearchSettingsEntity left,
        ResearchSettingsEntity right) =>
        left.SearchProviderPriority.SequenceEqual(
            right.SearchProviderPriority,
            StringComparer.Ordinal) &&
        left.CrawlerProviderPriority.SequenceEqual(
            right.CrawlerProviderPriority,
            StringComparer.Ordinal) &&
        string.Equals(left.ManagedResearchProvider, right.ManagedResearchProvider, StringComparison.OrdinalIgnoreCase) &&
        left.ManagedResearchDepth == right.ManagedResearchDepth;

    private static ResearchSettingsResponse ToResponse(ResearchSettingsEntity entity) => new(
        entity.GroundingMode,
        entity.ProfileModel,
        entity.GroundingModel,
        entity.DeepResearchModel,
        entity.AiSourceRerankingEnabled,
        entity.ProviderPreset,
        entity.SearchProviderPriority.AsReadOnly(),
        entity.CrawlerProviderPriority.AsReadOnly(),
        entity.UpdatedAt,
        entity.ManagedResearchProvider,
        entity.ManagedResearchDepth);
}
