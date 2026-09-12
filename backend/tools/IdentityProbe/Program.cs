using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;

const string userSecretsId = "989e5215-e80e-4c9f-b9a3-bdb809aa90fb";
const string defaultModel = "gemini-3.5-flash-lite";
const string promptTemplateVersion = "identity-resolution-v2";
const string SystemInstruction = """
You are RAVEN's bounded pre-search company identity assistant.
Use only existing model knowledge and the identity hints supplied by the user.
Do not browse, search, crawl, call tools, or invent unsupported entities.
When uncertain, ask for a useful hint or return Unknown. Do not expose hidden reasoning.
""";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true
};
var responseSchema = CreateResponseSchema();

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddEnvironmentVariables()
    .AddJsonFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", userSecretsId, "secrets.json"), optional: true)
    .Build();

var options = ReadOptions(configuration);
if (string.IsNullOrWhiteSpace(options.ApiKey))
{
    Console.Error.WriteLine("Gemini is not configured. Set the API key through Raven.Api user secrets or GEMINI_API_KEY.");
    return 2;
}

var selectedModel = FirstNonEmpty(
    configuration["GEMINI_IDENTITY_MODEL"],
    configuration["GEMINI_FAST_MODEL"],
    configuration["Providers:Gemini:FastModel"],
    defaultModel);

var scenarios = new[]
{
    new ProbeScenario("FPT", null, "family shorthand"),
    new ProbeScenario("FPT Software", null, "specific known company"),
    new ProbeScenario("Viettel", null, "family shorthand"),
    new ProbeScenario("Viettel Telecom", null, "specific known company"),
    new ProbeScenario("Vingroup", null, "specific known company"),
    new ProbeScenario("Vin", null, "short name"),
    new ProbeScenario("Masan", null, "specific known company"),
    new ProbeScenario("Sun", null, "short name"),
    new ProbeScenario("Sun Property", null, "name collision without geography"),
    new ProbeScenario("Sun Property", "Vietnam", "name collision with geography"),
    new ProbeScenario("Quang Minh Precision Components", null, "deliberately obscure"),
    new ProbeScenario("Viettel Technologies", null, "similar-name soundalike")
};

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute),
    Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds))
};

IAiModelProvider provider = new GeminiProvider(httpClient, Options.Create(options));
var results = new List<ProbeResult>(scenarios.Length);

Console.WriteLine($"Identity probe: model={selectedModel}; promptTemplate={promptTemplateVersion}; scenarios={scenarios.Length}");
Console.WriteLine("No Search, Crawl, or source evidence is used. Output is sanitized; raw model JSON is never printed.");
Console.WriteLine();

foreach (var scenario in scenarios)
{
    var result = await ProbeAsync(provider, selectedModel, scenario);
    results.Add(result);
    Print(result);
}

Console.WriteLine();
Console.WriteLine("Summary");
Console.WriteLine($"calls={results.Count}; providerSuccesses={results.Count(result => result.ProviderSucceeded)}; semanticParses={results.Count(result => result.StructuredParseSucceeded)}; failures={results.Count(result => result.FailureCode is not null)}");
return 0;

static GeminiOptions ReadOptions(IConfiguration configuration) => new()
{
    ApiKey = FirstNonEmpty(configuration["GEMINI_API_KEY"], configuration["Providers:Gemini:ApiKey"]),
    BaseUrl = FirstNonEmpty(configuration["GEMINI_BASE_URL"], configuration["Providers:Gemini:BaseUrl"], "https://generativelanguage.googleapis.com"),
    ApiVersion = FirstNonEmpty(configuration["GEMINI_API_VERSION"], configuration["Providers:Gemini:ApiVersion"], "v1beta"),
    FastModel = FirstNonEmpty(configuration["GEMINI_FAST_MODEL"], configuration["Providers:Gemini:FastModel"], defaultModel),
    TimeoutSeconds = ReadInt(configuration["GEMINI_TIMEOUT_SECONDS"], configuration["Providers:Gemini:TimeoutSeconds"], 60),
    MaxEvidenceCharacters = ReadInt(configuration["GEMINI_MAX_EVIDENCE_CHARACTERS"], configuration["Providers:Gemini:MaxEvidenceCharacters"], 120_000)
};

async Task<ProbeResult> ProbeAsync(
    IAiModelProvider provider,
    string model,
    ProbeScenario scenario)
{
    var hints = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = scenario.Name,
        ["country"] = scenario.Country
    };

    var request = new AiModelRequest(
        model,
        SystemInstruction,
        BuildPrompt(scenario),
        promptTemplateVersion,
        new AiEvidencePayload(hints, []),
        responseSchema);

    AiModelResult providerResult;
    try
    {
        providerResult = await provider.GenerateStructuredAsync(request);
    }
    catch (Exception exception)
    {
        return new ProbeResult(
            scenario,
            provider.Id,
            model,
            TimeSpan.Zero,
            null,
            null,
            false,
            false,
            "unhandled_exception",
            exception.GetType().Name);
    }

    if (!providerResult.StructuredJson.HasValue)
    {
        return new ProbeResult(
            scenario,
            providerResult.Provider,
            providerResult.Model,
            providerResult.Duration,
            providerResult.Usage,
            null,
            false,
            false,
            providerResult.Failure?.Code ?? "no_structured_result",
            providerResult.Failure?.Message);
    }

    try
    {
        var parsed = JsonSerializer.Deserialize<IdentityResponse>(
            providerResult.StructuredJson.Value.GetRawText(),
            jsonOptions);
        if (parsed is null || !TryNormalizeStatus(parsed.Status, out var status))
        {
            return new ProbeResult(
                scenario,
                providerResult.Provider,
                providerResult.Model,
                providerResult.Duration,
                providerResult.Usage,
                null,
                providerResult.Succeeded,
                false,
                "invalid_identity_schema",
                "Structured JSON did not contain a recognized identity status.");
        }

        var ambiguityType = NormalizeAmbiguityType(parsed.AmbiguityType);
        var entities = parsed.Entities?.Where(entity => !string.IsNullOrWhiteSpace(entity.DisplayName)).Take(8).ToArray() ?? [];
        var summary = new SemanticSummary(
            status,
            ambiguityType,
            parsed.RecommendedEntityId,
            entities.Select(entity => new EntitySummary(
                entity.DisplayName!,
                NormalizeNullable(entity.EntityType),
                NormalizeNullable(entity.Country),
                NormalizeNullable(entity.OfficialDomain),
                NormalizeNullable(entity.RelationshipToQuery),
                NormalizeNullable(entity.Confidence))).ToArray(),
            parsed.RequestedHints?.Select(NormalizeNullable).Where(value => value is not null).Cast<string>().Take(5).ToArray() ?? [],
            NormalizeNullable(parsed.Message));

        return new ProbeResult(
            scenario,
            providerResult.Provider,
            providerResult.Model,
            providerResult.Duration,
            providerResult.Usage,
            summary,
            providerResult.Succeeded,
            true,
            providerResult.Failure?.Code,
            providerResult.Failure?.Message);
    }
    catch (JsonException)
    {
        return new ProbeResult(
            scenario,
            providerResult.Provider,
            providerResult.Model,
            providerResult.Duration,
            providerResult.Usage,
            null,
            providerResult.Succeeded,
            false,
            "invalid_identity_schema",
            "Structured JSON could not be mapped to the identity probe schema.");
    }
}

static string BuildPrompt(ProbeScenario scenario) => $"""
Resolve which real-world organization the user means from identity hints only.

Use existing model knowledge only. Do not browse, search, crawl, request tools, or use source evidence.
Do not invent entities or relationships to fill the schema. If the identity is not safely known, return NeedsMoreInfo or Unknown.
Distinguish a corporate-family shorthand from unrelated same-name or similar-name organizations.
For a family shorthand, return the known parent first and at most six high-confidence relevant children; do not dump a holdings list.
For a specific company, resolve that target without forcing a family chooser merely because it has a parent.
For a name collision, return at most four plausible matches, or request a useful hint such as Country or Website.
Official domains, legal names, and parent relationships are optional identity/search hints, not verified profile facts.
Keep descriptions short and do not provide broad company-profile facts or hidden reasoning.
Return only the required structured JSON.

Name: {scenario.Name}
Country: {scenario.Country ?? "null"}
""";

static void Print(ProbeResult result)
{
    var summary = result.Semantic is null
        ? "semantic=n/a"
        : $"status={result.Semantic.Status}; ambiguity={result.Semantic.AmbiguityType}; entities={result.Semantic.Entities.Count}; requestedHints={string.Join(',', result.Semantic.RequestedHints)}";
    var usage = result.Usage is null
        ? "tokens=n/a"
        : $"tokens=in:{Format(result.Usage.PromptTokens)},out:{Format(result.Usage.OutputTokens)},cached:{Format(result.Usage.CachedInputTokens)},thinking:{Format(result.Usage.ThinkingTokens)},total:{Format(result.Usage.TotalTokens)}";
    var failure = result.FailureCode is null ? "failure=none" : $"failure={result.FailureCode}";
    Console.WriteLine($"{result.Scenario.Name} [{result.Scenario.Country ?? "no-country"}] | {summary} | model={result.Model} | durationMs={result.Duration.TotalMilliseconds:F0} | {usage} | providerSucceeded={result.ProviderSucceeded} | structuredParse={result.StructuredParseSucceeded} | {failure}");

    if (result.Semantic is not null)
    {
        foreach (var entity in result.Semantic.Entities)
        {
            var location = string.IsNullOrWhiteSpace(entity.Country) ? string.Empty : $" ({entity.Country})";
            var kind = string.IsNullOrWhiteSpace(entity.EntityType) ? "unknown" : entity.EntityType;
            Console.WriteLine($"  - {entity.DisplayName}{location}; type={kind}; relationship={entity.RelationshipToQuery ?? "n/a"}; confidence={entity.Confidence ?? "n/a"}; domain={entity.OfficialDomain ?? "n/a"}");
        }
    }
}

static bool TryNormalizeStatus(string? raw, out string status)
{
    status = NormalizeToken(raw);
    if (status is "RESOLVED" or "AMBIGUOUS" or "NEEDSMOREINFO" or "UNKNOWN")
    {
        status = status switch
        {
            "NEEDSMOREINFO" => "NeedsMoreInfo",
            _ => char.ToUpperInvariant(status[0]) + status[1..].ToLowerInvariant()
        };
        return true;
    }

    status = string.Empty;
    return false;
}

static string NormalizeAmbiguityType(string? raw) => NormalizeToken(raw) switch
{
    "CORPORATEFAMILY" => "CorporateFamily",
    "NAMECOLLISION" => "NameCollision",
    "UNCLEAR" => "Unclear",
    _ => "None"
};

static string NormalizeToken(string? value) => string.Concat((value ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();

static string? NormalizeNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 160)];

static int ReadInt(string? first, string? second, int fallback) => int.TryParse(FirstNonEmpty(first, second), out var value) && value > 0 ? value : fallback;

static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

static string Format(int? value) => value?.ToString() ?? "n/a";

static JsonElement CreateResponseSchema()
{
using var schemaDocument = JsonDocument.Parse("""
{
  "type": "object",
  "properties": {
    "status": { "type": "string", "enum": ["Resolved", "Ambiguous", "NeedsMoreInfo", "Unknown"] },
    "ambiguityType": { "type": "string", "enum": ["None", "CorporateFamily", "NameCollision", "Unclear"] },
    "recommendedEntityId": { "type": "string", "nullable": true },
    "entities": {
      "type": "array",
      "maxItems": 8,
      "items": {
        "type": "object",
        "properties": {
          "temporaryId": { "type": "string" },
          "displayName": { "type": "string" },
          "legalName": { "type": "string", "nullable": true },
          "country": { "type": "string", "nullable": true },
          "region": { "type": "string", "nullable": true },
          "officialDomain": { "type": "string", "nullable": true },
          "entityType": { "type": "string" },
          "parentTemporaryId": { "type": "string", "nullable": true },
          "relationshipToQuery": { "type": "string" },
          "confidence": { "type": "string", "enum": ["High", "Medium", "Low"] },
          "shortDescription": { "type": "string", "nullable": true }
        }
      }
    },
    "requestedHints": { "type": "array", "maxItems": 3, "items": { "type": "string" } },
    "message": { "type": "string", "nullable": true }
  },
  "required": ["status", "ambiguityType", "entities", "requestedHints"]
}
""");
return schemaDocument.RootElement.Clone();
}

sealed record ProbeScenario(string Name, string? Country, string Kind);

sealed record ProbeResult(
    ProbeScenario Scenario,
    string Provider,
    string Model,
    TimeSpan Duration,
    AiUsage? Usage,
    SemanticSummary? Semantic,
    bool ProviderSucceeded,
    bool StructuredParseSucceeded,
    string? FailureCode,
    string? FailureMessage);

sealed record SemanticSummary(
    string Status,
    string AmbiguityType,
    string? RecommendedEntityId,
    IReadOnlyList<EntitySummary> Entities,
    IReadOnlyList<string> RequestedHints,
    string? Message);

sealed record EntitySummary(
    string DisplayName,
    string? EntityType,
    string? Country,
    string? OfficialDomain,
    string? RelationshipToQuery,
    string? Confidence);

sealed class IdentityResponse
{
    public string? Status { get; set; }
    public string? AmbiguityType { get; set; }
    public string? RecommendedEntityId { get; set; }
    public List<IdentityEntity>? Entities { get; set; }
    public List<string>? RequestedHints { get; set; }
    public string? Message { get; set; }
}

sealed class IdentityEntity
{
    public string? DisplayName { get; set; }
    public string? Country { get; set; }
    public string? OfficialDomain { get; set; }
    public string? EntityType { get; set; }
    public string? RelationshipToQuery { get; set; }
    public string? Confidence { get; set; }
}
