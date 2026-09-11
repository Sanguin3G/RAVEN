using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Settings;

/// <summary>
/// SQLite-backed store for the one application-wide research-settings record.
/// The store copies values at its boundary so callers cannot accidentally alter
/// an EF-tracked settings instance after a read.
/// </summary>
public sealed class EfResearchSettingsStore(RavenDbContext dbContext) : IResearchSettingsStore
{
    public async Task<ResearchSettingsEntity?> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await dbContext.ResearchSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == ResearchSettingsEntity.SingletonKey, cancellationToken);

        return settings?.Clone();
    }

    public async Task SaveAsync(ResearchSettingsEntity settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var existing = await dbContext.ResearchSettings
            .SingleOrDefaultAsync(item => item.Id == ResearchSettingsEntity.SingletonKey, cancellationToken);

        if (existing is null)
        {
            dbContext.ResearchSettings.Add(settings.Clone());
        }
        else
        {
            existing.GroundingMode = settings.GroundingMode;
            existing.ProfileModel = settings.ProfileModel;
            existing.GroundingModel = settings.GroundingModel;
            existing.DeepResearchModel = settings.DeepResearchModel;
            existing.AiSourceRerankingEnabled = settings.AiSourceRerankingEnabled;
            existing.ProviderPreset = settings.ProviderPreset;
            existing.SearchProviderPriority = [.. settings.SearchProviderPriority];
            existing.CrawlerProviderPriority = [.. settings.CrawlerProviderPriority];
            existing.UpdatedAt = settings.UpdatedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
