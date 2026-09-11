namespace Raven.Api.Features.Settings;

/// <summary>
/// Indicates that a settings update is not safe to persist. The messages are
/// suitable for a 400 response; they never include secret configuration values.
/// </summary>
public sealed class ResearchSettingsValidationException : ArgumentException
{
    public ResearchSettingsValidationException(IReadOnlyList<string> errors)
        : base(string.Join(" ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
