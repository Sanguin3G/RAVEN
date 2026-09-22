using System.Threading.Channels;

namespace Raven.Api.Features.DeepResearch;

/// <summary>
/// Small in-process work queue for Deep Research. It intentionally provides no
/// distributed delivery guarantee: runs execute only while the API is running.
/// The durable run and activity rows remain available for polling and recovery.
/// </summary>
public interface IDeepResearchQueue
{
    ValueTask EnqueueAsync(Guid runId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken = default);
}

public sealed class DeepResearchQueue : IDeepResearchQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public ValueTask EnqueueAsync(Guid runId, CancellationToken cancellationToken = default) =>
        channel.Writer.WriteAsync(runId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken = default) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Executes queued work within an application scope, keeping HTTP requests
/// short while the browser polls the persisted run state.
/// </summary>
public sealed class DeepResearchWorker(
    IDeepResearchQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DeepResearchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var runId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IDeepResearchRunService>();
                await service.ExecuteAsync(runId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The run service records a safe public failure state. Logging
                // here intentionally does not include question/evidence content.
                logger.LogError(exception, "Deep Research worker failed for run {RunId}", runId);
            }
        }
    }
}
