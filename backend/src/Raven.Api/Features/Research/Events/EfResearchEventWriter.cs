using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Research.Events;

/// <summary>EF-backed event sink. Sanitization happens before persistence.</summary>
public sealed class EfResearchEventWriter(RavenDbContext dbContext) : IResearchEventWriter
{
    public async Task WriteAsync(ResearchEvent researchEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(researchEvent);
        var safeEvent = ResearchEventSanitizer.Sanitize(researchEvent);

        if (safeEvent.ResearchRunId is not null && safeEvent.Sequence == 0)
        {
            var lastSequence = await dbContext.ResearchEvents
                .Where(item => item.ResearchRunId == safeEvent.ResearchRunId)
                .Select(item => (long?)item.Sequence)
                .MaxAsync(cancellationToken) ?? 0;
            safeEvent = safeEvent with { Sequence = lastSequence + 1 };
        }

        dbContext.ResearchEvents.Add(safeEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
