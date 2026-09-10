namespace Raven.Api.Features.Ai;

public sealed class GeminiOptions
{
    public const string SectionName = "Providers:Gemini";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";

    /// <summary>
    /// Bound from the server-side GEMINI_API_KEY environment variable or user
    /// secrets. Never expose this value through API responses or logs.
    /// </summary>
    public string? ApiKey { get; set; }

    public string ApiVersion { get; set; } = "v1beta";

    // Product plan defaults. The selected model is supplied on each request.
    public string FastModel { get; set; } = "gemini-3.5-flash-lite";

    public string DeepModel { get; set; } = "gemini-3.8-flash";

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of evidence characters included in one request. The
    /// model boundary must remain bounded until retrieval/RAG is introduced.
    /// </summary>
    public int MaxEvidenceCharacters { get; set; } = 120_000;
}
