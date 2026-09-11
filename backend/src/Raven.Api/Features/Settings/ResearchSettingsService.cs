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

    public async Task<ResearchSettingsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var persisted = await store.GetAsync(cancellationToken);

        if (persisted is null)
        {
            var defaults = ResearchSettingsDefaults.CreateEntity();
            await store.SaveAsync(defaults, cancellationToken);
            return ToResponse(defaults);
        }

        var validationErrors = ValidateEntity(persisted);
        if (validationErrors.Count == 0 && persisted.Id == ResearchSettingsEntity.SingletonKey)
        {
            return ToResponse(persisted);
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
            AiSourceRerankingEnabled = request.AiSourceRerankingEnabled,
            ProviderPreset = request.ProviderPreset,
            SearchProviderPriority = NormalizeProviderIds(
                request.SearchProviderPriority ?? current.SearchProviderPriority),
            CrawlerProviderPriority = NormalizeProviderIds(
                request.CrawlerProviderPriority ?? current.CrawlerProviderPriority),
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

        if (!Enum.IsDefined(request.ProviderPreset))
        {
            errors.Add("Provider preset is not supported.");
        }

        AddProviderErrors(errors, request.SearchProviderPriority, "Search");
        AddProviderErrors(errors, request.CrawlerProviderPriority, "Crawler");

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
            settings.CrawlerProviderPriority));

        if (string.IsNullOrWhiteSpace(settings.Id))
        {
            errors.Add("Settings identity is missing.");
        }

        return errors;
    }

    private static void AddProviderErrors(
        ICollection<string> errors,
        IReadOnlyList<string>? providerIds,
        string category)
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
    }

    private static string NormalizeModel(string model) => model.Trim();

    private static List<string> NormalizeProviderIds(IReadOnlyList<string> providerIds) =>
        providerIds
            .Select(providerId => providerId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ResearchSettingsResponse ToResponse(ResearchSettingsEntity entity) => new(
        entity.GroundingMode,
        entity.ProfileModel,
        entity.GroundingModel,
        entity.DeepResearchModel,
        entity.AiSourceRerankingEnabled,
        entity.ProviderPreset,
        entity.SearchProviderPriority.AsReadOnly(),
        entity.CrawlerProviderPriority.AsReadOnly(),
        entity.UpdatedAt);
}
