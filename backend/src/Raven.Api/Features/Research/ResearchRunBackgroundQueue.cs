using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Raven.Api.Features.Research;

public interface IResearchRunBackgroundQueue
{
    ValueTask EnqueueAsync(Guid runId, Guid companyId, DiscoverResearchRequest request, CancellationToken cancellationToken = default);
    bool Cancel(Guid runId);
}

public sealed record ResearchRunWork(Guid RunId, Guid CompanyId, DiscoverResearchRequest Request);

public sealed class ResearchRunBackgroundQueue : BackgroundService, IResearchRunBackgroundQueue
{
    private readonly Channel<ResearchRunWork> queue = Channel.CreateUnbounded<ResearchRunWork>();
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> active = new();

    public ResearchRunBackgroundQueue(IServiceScopeFactory scopeFactory) => this.scopeFactory = scopeFactory;

    public ValueTask EnqueueAsync(Guid runId, Guid companyId, DiscoverResearchRequest request, CancellationToken cancellationToken = default) =>
        queue.Writer.WriteAsync(new ResearchRunWork(runId, companyId, request), cancellationToken);

    public bool Cancel(Guid runId)
    {
        if (!active.TryGetValue(runId, out var cancellation)) return false;
        cancellation.Cancel();
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            active[work.RunId] = cancellation;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var research = scope.ServiceProvider.GetRequiredService<IResearchCompanyService>();
                await research.DiscoverAsync(work.CompanyId, work.Request with { ResearchRunId = work.RunId }, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // The explicit cancel endpoint owns the persisted terminal state.
            }
            catch
            {
                // The research service records provider failures on the run. Keep the
                // worker alive for the next company rather than crashing the host.
            }
            finally
            {
                active.TryRemove(work.RunId, out _);
            }
        }
    }
}
