using Microsoft.Extensions.Options;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>
/// Polls durable managed jobs in isolated scopes. The job row, not the channel,
/// is the source of truth, so queued/researching jobs can be re-enqueued after a
/// process restart by the worker registration.
/// </summary>
public sealed class ManagedResearchWorker(
    IManagedResearchJobQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<ExaAgentOptions> options,
    ILogger<ManagedResearchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueActiveJobsAsync(stoppingToken);

        await foreach (var jobId in queue.DequeueAllAsync(stoppingToken))
        {
            ManagedResearchJobResponse? response = null;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IManagedResearchJobService>();
                response = await service.ProcessAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The job service persists provider/application failures. This
                // log contains only the job ID and safe exception category.
                logger.LogError(exception, "Managed research worker failed for job {JobId}", jobId);
            }

            if (response?.Status == ManagedResearchJobStatus.Researching)
            {
                try
                {
                    await Task.Delay(GetPollInterval(), stoppingToken);
                    await queue.EnqueueAsync(jobId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task RequeueActiveJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IManagedResearchJobStore>();
            foreach (var job in await store.ListActiveAsync(cancellationToken))
            {
                await queue.EnqueueAsync(job.Id, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A registration/startup failure should remain visible in logs; the
            // durable rows remain available for the next worker attempt.
            logger.LogError(exception, "Managed research worker could not restore active jobs");
        }
    }

    private TimeSpan GetPollInterval() => TimeSpan.FromMilliseconds(
        Math.Clamp(options.Value.PollIntervalMilliseconds, 250, 60_000));
}
