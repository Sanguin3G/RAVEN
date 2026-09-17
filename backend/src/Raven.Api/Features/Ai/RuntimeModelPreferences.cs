using Microsoft.Extensions.Options;

namespace Raven.Api.Features.Ai;

/// <summary>
/// Holds the explicit model choices made from the local workspace Settings UI.
/// These choices are deliberately runtime-only: deployment configuration remains
/// the durable source of truth, and no secret configuration is exposed or edited.
/// </summary>
public interface IRuntimeModelPreferences
{
    RuntimeModelPreferenceResponse Current { get; }

    bool TryUpdate(UpdateRuntimeModelPreferencesRequest request, out RuntimeModelPreferenceResponse preferences);
}

public sealed record RuntimeModelPreferenceResponse(string FastModel);

public sealed record UpdateRuntimeModelPreferencesRequest(string FastModel);

public sealed class RuntimeModelPreferences : IRuntimeModelPreferences
{
    public const string FlashLite = "gemini-3.5-flash-lite";
    public const string Flash = "gemini-3.8-flash";

    private static readonly HashSet<string> AllowedModels = [FlashLite, Flash];
    private readonly object sync = new();
    private RuntimeModelPreferenceResponse current;

    public RuntimeModelPreferences(IOptions<GeminiOptions> options)
    {
        var configured = options.Value;
        current = new RuntimeModelPreferenceResponse(
            AllowedOrDefault(configured.FastModel, FlashLite));
    }

    public RuntimeModelPreferenceResponse Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public bool TryUpdate(UpdateRuntimeModelPreferencesRequest request, out RuntimeModelPreferenceResponse preferences)
    {
        if (request is null || !AllowedModels.Contains(request.FastModel))
        {
            preferences = Current;
            return false;
        }

        lock (sync)
        {
            current = new RuntimeModelPreferenceResponse(request.FastModel);
            preferences = current;
            return true;
        }
    }

    private static string AllowedOrDefault(string? model, string fallback) =>
        !string.IsNullOrWhiteSpace(model) && AllowedModels.Contains(model) ? model : fallback;
}
