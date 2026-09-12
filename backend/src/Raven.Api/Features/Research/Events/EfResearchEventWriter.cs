using Raven.Api.Data;

namespace Raven.Api.Features.Research.Events;

/// <summary>EF-backed event sink. Sanitization happens before persistence.</summary>
public sealed class EfResearchEventWriter(RavenDbContext dbContext) : IResearchEventWriter
{
    private static long sequenceClock;

    public async Task WriteAsync(ResearchEvent researchEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(researchEvent);
        var safeEvent = ResearchEventSanitizer.Sanitize(researchEvent);

        if (safeEvent.Sequence == 0)
        {
            safeEvent = safeEvent with { Sequence = NextSequence() };
        }

        dbContext.ResearchEvents.Add(safeEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// ResearchEvents retain a sequence index for existing readers, but sequence
    /// allocation must not issue a MAX query for every write. UTC ticks provide a
    /// useful cross-run ordering and the atomic increment handles same-tick
    /// writes in this process.
    /// </summary>
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
}
