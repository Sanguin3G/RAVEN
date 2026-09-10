namespace Raven.Api.Features.Research.Events;

/// <summary>
/// Application boundary for recording research activity. The implementation may
/// persist to EF, a test collection, or another store; event production does not
/// depend on a particular database.
/// </summary>
public interface IResearchEventWriter
{
    Task WriteAsync(ResearchEvent researchEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decorator for the application boundary. It makes redaction and bounding a
/// property of the write path, even when a caller forgets to invoke the helper.
/// </summary>
public sealed class SanitizingResearchEventWriter(IResearchEventWriter inner) : IResearchEventWriter
{
    public Task WriteAsync(ResearchEvent researchEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(researchEvent);
        return inner.WriteAsync(ResearchEventSanitizer.Sanitize(researchEvent), cancellationToken);
    }
}
