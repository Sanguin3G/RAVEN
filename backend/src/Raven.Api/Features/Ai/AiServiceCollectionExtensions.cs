using Microsoft.Extensions.DependencyInjection;

namespace Raven.Api.Features.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the neutral AI provider boundary and Gemini REST adapter.
    /// Call from Program.cs with:
    /// <c>builder.Services.AddGeminiAi(builder.Configuration);</c>
    /// </summary>
    public static IServiceCollection AddGeminiAi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.PostConfigure<GeminiOptions>(options =>
        {
            options.ApiKey = configuration["GEMINI_API_KEY"] ?? options.ApiKey;
            options.BaseUrl = configuration["GEMINI_BASE_URL"] ?? options.BaseUrl;
            options.ApiVersion = configuration["GEMINI_API_VERSION"] ?? options.ApiVersion;
            options.FastModel = configuration["GEMINI_FAST_MODEL"] ?? options.FastModel;
            options.DeepModel = configuration["GEMINI_DEEP_MODEL"] ?? options.DeepModel;

            if (int.TryParse(configuration["GEMINI_TIMEOUT_SECONDS"], out var timeoutSeconds))
            {
                options.TimeoutSeconds = timeoutSeconds;
            }

            if (int.TryParse(configuration["GEMINI_MAX_EVIDENCE_CHARACTERS"], out var maxEvidenceCharacters))
            {
                options.MaxEvidenceCharacters = maxEvidenceCharacters;
            }
        });

        services.AddHttpClient<GeminiProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeminiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        });
        services.AddScoped<IAiModelProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<GeminiProvider>());

        return services;
    }
}
