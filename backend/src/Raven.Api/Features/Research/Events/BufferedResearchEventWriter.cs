using System.Threading.Channels;

namespace Raven.Api.Features.Research.Events;

/// <summary>
/// Bounded, process-local transport for diagnostic execution telemetry. Writes
/// are deliberately non-blocking: product workflows must not wait for SQLite
/// merely to record a provider timing.
/// </summary>
public sealed class BufferedResearchEventWriter(ILogger<BufferedResearchEventWriter> logger) : IResearchEventWriter, IResearchTelemetryFlusher
{
    internal const int Capacity = 512;
    internal const int BatchSize = 32;
    internal static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);

    private static long sequenceClock;
    private readonly Channel<QueueItem> queue = Channel.CreateBounded<QueueItem>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
    private long droppedCount;

    internal ChannelReader<QueueItem> Reader => queue.Reader;
    internal long DroppedCount => Interlocked.Read(ref droppedCount);

    public Task WriteAsync(ResearchEvent researchEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(researchEvent);
        var safe = ResearchEventSanitizer.Sanitize(researchEvent);
        if (safe.Sequence == 0)
        {
            safe = safe with { Sequence = NextSequence() };
        }

        // TryWrite is intentional. A full diagnostics buffer is observable via
        // the counter/logger but never backpressures research or chat.
        if (!queue.Writer.TryWrite(new EventItem(safe)))
        {
            var dropped = Interlocked.Increment(ref droppedCount);
            if (dropped == 1 || dropped % 64 == 0)
            {
                logger.LogWarning("Research execution telemetry queue is full; {DroppedTelemetryCount} diagnostic events have been dropped.", dropped);
            }
        }

        return Task.CompletedTask;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var marker = new FlushItem(completion);
        if (!queue.Writer.TryWrite(marker))
        {
            try
            {
                await queue.Writer.WriteAsync(marker, cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return;
            }
        }

        await completion.Task.WaitAsync(cancellationToken);
    }

    internal void Complete() => queue.Writer.TryComplete();

    private static long NextSequence()
    {
        var now = DateTimeOffset.UtcNow.UtcTicks;
        while (true)
        {
            var previous = Interlocked.Read(ref sequenceClock);
            var next = Math.Max(now, previous + 1);
            if (Interlocked.CompareExchange(ref sequenceClock, next, previous) == previous)
            {
                return next;
            }
        }
    }

    internal abstract record QueueItem;
    internal sealed record EventItem(ResearchEvent Event) : QueueItem;
    internal sealed record FlushItem(TaskCompletionSource Completion) : QueueItem;
}

/// <summary>Completion boundary for callers that immediately read execution summaries.</summary>
public interface IResearchTelemetryFlusher
{
    Task FlushAsync(CancellationToken cancellationToken = default);
}
