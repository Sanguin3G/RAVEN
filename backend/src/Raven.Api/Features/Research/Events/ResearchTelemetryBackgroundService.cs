using Microsoft.Extensions.DependencyInjection;
using Raven.Api.Data;

namespace Raven.Api.Features.Research.Events;

/// <summary>
/// Persists diagnostic events with a new EF scope per batch. It never captures
/// the request scope that produced the event.
/// </summary>
public sealed class ResearchTelemetryBackgroundService(
    BufferedResearchEventWriter transport,
    IServiceScopeFactory scopeFactory,
    ILogger<ResearchTelemetryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ResearchEvent>(BufferedResearchEventWriter.BatchSize);
        try
        {
            // The producer is completed in StopAsync. Do not bind this drain to
            // the host stopping token: queued diagnostics should get their
            // best-effort final batch before process shutdown.
            await foreach (var item in transport.Reader.ReadAllAsync())
            {
                switch (item)
                {
                    case BufferedResearchEventWriter.EventItem eventItem:
                        batch.Add(eventItem.Event);
                        if (batch.Count >= BufferedResearchEventWriter.BatchSize)
                        {
                            await PersistAsync(batch, CancellationToken.None);
                        }
                        else
                        {
                            await CollectForIntervalAsync(batch, CancellationToken.None);
                        }
                        break;
                    case BufferedResearchEventWriter.FlushItem flush:
                        await PersistAsync(batch, CancellationToken.None);
                        flush.Completion.TrySetResult();
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // StopAsync completes the writer first; this only covers a forced host timeout.
        }
        finally
        {
            await PersistAsync(batch, CancellationToken.None);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        transport.Complete();
        await base.StopAsync(cancellationToken);
    }

    private async Task CollectForIntervalAsync(List<ResearchEvent> batch, CancellationToken cancellationToken)
    {
        using var delay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        delay.CancelAfter(BufferedResearchEventWriter.FlushInterval);
        try
        {
            while (batch.Count < BufferedResearchEventWriter.BatchSize &&
                   await transport.Reader.WaitToReadAsync(delay.Token))
            {
                while (batch.Count < BufferedResearchEventWriter.BatchSize && transport.Reader.TryRead(out var queued))
                {
                    if (queued is BufferedResearchEventWriter.EventItem eventItem)
                    {
                        batch.Add(eventItem.Event);
                        continue;
                    }

                    if (queued is BufferedResearchEventWriter.FlushItem flush)
                    {
                        await PersistAsync(batch, cancellationToken);
                        flush.Completion.TrySetResult();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The coalescing window elapsed; persist below.
        }

        await PersistAsync(batch, cancellationToken);
    }

    private async Task PersistAsync(List<ResearchEvent> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;
        var pending = batch.ToArray();
        batch.Clear();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
            dbContext.ResearchEvents.AddRange(pending);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not persist {TelemetryCount} execution telemetry events; diagnostics were dropped.", pending.Length);
        }
    }
}
