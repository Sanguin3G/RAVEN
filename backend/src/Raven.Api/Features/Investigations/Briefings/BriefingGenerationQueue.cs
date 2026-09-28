using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;

namespace Raven.Api.Features.Research.Briefings;

public sealed class BriefingGenerationQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class BriefingGenerationWorker(
    BriefingGenerationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<BriefingGenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueActiveAsync(stoppingToken);
        await foreach (var jobId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<BriefingGenerationService>()
                    .ProcessAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Briefing generation worker failed for job {JobId}", jobId);
            }
        }
    }

    private async Task RequeueActiveAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Raven.Api.Data.RavenDbContext>();
            var activeJobs = await db.BriefingGenerationJobs.AsNoTracking()
                .Where(job => job.Status == BriefingGenerationStatus.Queued ||
                              job.Status == BriefingGenerationStatus.Generating ||
                              job.Status == BriefingGenerationStatus.Saving)
                .Select(job => new { job.Id, job.CreatedAt }).ToArrayAsync(ct);
            var ids = activeJobs.OrderBy(job => job.CreatedAt).Select(job => job.Id).ToArray();
            foreach (var id in ids) await queue.EnqueueAsync(id, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogError(exception, "Briefing worker could not restore active jobs"); }
    }
}
