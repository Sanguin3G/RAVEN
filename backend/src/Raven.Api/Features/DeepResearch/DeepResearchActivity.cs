namespace Raven.Api.Features.DeepResearch;

/// <summary>Creates safe activity payloads for the public timeline.</summary>
public static class DeepResearchActivity
{
    public static DeepResearchActivityEvent RunStarted() =>
        New("run", DeepResearchActivityStatus.Working, "Deep Research started");

    public static DeepResearchActivityEvent RunCompleted() =>
        New("run", DeepResearchActivityStatus.Completed, "Deep Research completed");

    public static DeepResearchActivityEvent RunFailed(string? detail = null) =>
        New("run", DeepResearchActivityStatus.Failed, "Deep Research failed", detail);

    public static DeepResearchActivityEvent RunCancelled() =>
        New("run", DeepResearchActivityStatus.Skipped, "Deep Research cancelled");

    public static DeepResearchActivityEvent ToolStarted(string label) =>
        New("tool", DeepResearchActivityStatus.Working, label);

    public static DeepResearchActivityEvent ToolCompleted(string label, string? detail = null, string? provider = null, IReadOnlyList<Guid>? sourceDocumentIds = null) =>
        New("tool", DeepResearchActivityStatus.Completed, label, detail, provider, sourceDocumentIds);

    public static DeepResearchActivityEvent ToolFailed(string label, string? detail = null, string? provider = null) =>
        New("tool", DeepResearchActivityStatus.Failed, label, detail, provider);

    public static DeepResearchActivityEvent BudgetExhausted(string label) =>
        New("budget", DeepResearchActivityStatus.Skipped, label, "The configured Deep Research budget was reached.");

    private static DeepResearchActivityEvent New(
        string type,
        DeepResearchActivityStatus status,
        string label,
        string? detail = null,
        string? provider = null,
        IReadOnlyList<Guid>? sourceDocumentIds = null) =>
        DeepResearchActivitySanitizer.Sanitize(new DeepResearchActivityEvent(type, status, label, detail, provider, sourceDocumentIds));
}

public static class DeepResearchActivitySanitizer
{
    public const int MaxLabelLength = 120;
    public const int MaxDetailLength = 280;
    public const int MaxProviderLength = 100;
    public const int MaxSourceDocumentIds = 12;

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "run", "tool", "search", "crawl", "source", "result", "budget"
    };

    public static DeepResearchActivityEvent Sanitize(DeepResearchActivityEvent activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var rawType = activity.Type?.Trim() ?? string.Empty;
        var type = AllowedTypes.Contains(rawType)
            ? rawType.ToLowerInvariant()
            : "tool";
        var label = Bound(activity.Label, MaxLabelLength) ?? "Activity";
        var detail = Bound(activity.Detail, MaxDetailLength);
        var provider = Bound(activity.Provider, MaxProviderLength);
        var ids = (activity.SourceDocumentIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(MaxSourceDocumentIds)
            .ToArray();

        return new DeepResearchActivityEvent(type, activity.Status, label, detail, provider, ids);
    }

    private static string? Bound(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized[..Math.Min(maxLength, normalized.Length)];
    }
}
