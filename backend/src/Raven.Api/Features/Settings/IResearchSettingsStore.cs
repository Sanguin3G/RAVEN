namespace Raven.Api.Features.Settings;

/// <summary>
/// Persistence boundary for the singleton research settings row.
/// Implementations must treat returned/saved entities as detached values so the
/// service cannot accidentally mutate a tracked EF entity outside a save call.
/// </summary>
public interface IResearchSettingsStore
{
    Task<ResearchSettingsEntity?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ResearchSettingsEntity settings, CancellationToken cancellationToken = default);
}

/// <summary>
/// Small reference store for isolated tests and local composition before the EF
/// migration is integrated. It is intentionally not a durable production store.
/// </summary>
public sealed class InMemoryResearchSettingsStore : IResearchSettingsStore
{
    private readonly object sync = new();
    private ResearchSettingsEntity? current;

    public Task<ResearchSettingsEntity?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            return Task.FromResult(current?.Clone());
        }
    }

    public Task SaveAsync(ResearchSettingsEntity settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            current = settings.Clone();
        }

        return Task.CompletedTask;
    }
}
