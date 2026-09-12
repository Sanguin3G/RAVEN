using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;

const string secretsId = "989e5215-e80e-4c9f-b9a3-bdb809aa90fb";
const string model = "gemini-3.5-flash-lite";
const string template = "identity-topology-v2";
const string SystemInstruction = """
You are RAVEN's bounded company-identity topology assistant. Model knowledge may describe candidates and relationships, but deterministic RAVEN policy decides workflow state. Never decide which organization the user intends.
""";

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddJsonFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", secretsId, "secrets.json"), optional: true)
    .Build();
var options = new GeminiOptions
{
    ApiKey = configuration["GEMINI_API_KEY"] ?? configuration["Providers:Gemini:ApiKey"],
    BaseUrl = configuration["GEMINI_BASE_URL"] ?? "https://generativelanguage.googleapis.com",
    ApiVersion = configuration["GEMINI_API_VERSION"] ?? "v1beta",
    TimeoutSeconds = 60
};
if (string.IsNullOrWhiteSpace(options.ApiKey))
{
    Console.Error.WriteLine("Gemini is not configured.");
    return 2;
}

var scenarios = new (string Name, string? Country)[]
{
    ("FPT", null), ("FPT Software", null), ("Viettel", null), ("Viettel Telecom", null),
    ("Vingroup", null), ("Vin", null), ("Masan", null), ("Sun Property", null),
    ("Sun Property", "Vietnam"), ("Quang Minh Precision Components", null), ("Viettel Technologies", null)
};
using var client = new HttpClient { BaseAddress = new Uri(options.BaseUrl), Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
IAiModelProvider provider = new GeminiProvider(client, Options.Create(options));
var schema = CreateSchema();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

Console.WriteLine($"Identity topology probe: model={model}; template={template}; scenarios={scenarios.Length}");
Console.WriteLine("No Search, Crawl, evidence, Company, or ResearchRun is used. Raw prompts and responses are not printed.");
foreach (var scenario in scenarios)
{
    var request = new AiModelRequest(model, SystemInstruction, Prompt(scenario.Name, scenario.Country), template,
        new AiEvidencePayload(new Dictionary<string, string?> { ["name"] = scenario.Name, ["country"] = scenario.Country }, []), schema);
    var result = await provider.GenerateStructuredAsync(request);
    var topology = result.StructuredJson is { } payload ? JsonSerializer.Deserialize<Topology>(payload.GetRawText(), json) : null;
    var valid = topology is not null && IsInterpretation(topology.Interpretation);
    var entities = topology?.Candidates?.Where(x => !string.IsNullOrWhiteSpace(x.DisplayName)).Take(7).ToArray() ?? [];
    var hints = topology?.RequestedHints?.Where(IsHint).Take(3).ToArray() ?? [];
    Console.WriteLine($"{scenario.Name} [{scenario.Country ?? "no-country"}] | interpretation={(valid ? topology!.Interpretation : "invalid")}; candidates={entities.Length}; hints={string.Join(',', hints)}; durationMs={result.Duration.TotalMilliseconds:F0}; tokens=in:{result.Usage?.PromptTokens?.ToString() ?? "n/a"},out:{result.Usage?.OutputTokens?.ToString() ?? "n/a"}; parse={valid}; failure={result.Failure?.Code ?? "none"}");
    foreach (var entity in entities)
        Console.WriteLine($"  - {Bound(entity.DisplayName)}; type={Bound(entity.EntityType)}; relation={Bound(entity.RelationshipToQuery)}; parent={Bound(entity.ParentTemporaryId)}; confidence={Bound(entity.Confidence)}; country={Bound(entity.Country)}; domain={Bound(entity.OfficialDomain)}");
}
return 0;

static string Prompt(string name, string? country) => $"""
Describe the company identity topology from the supplied hints. You identify possible organizations; you never choose the user's intent and you never return a workflow status.
Use existing model knowledge only. Do not browse, search, crawl, request tools, or use sources. Do not invent entities or relationships.
Return CorporateFamilyShorthand when the input itself commonly acts as an umbrella/group name that plausibly denotes multiple meaningful family organizations, even if the parent is canonical or famous. Return parent first and at most six relevant high-confidence children. A parent is not automatically the intended target.
Return SpecificEntity only when the input sufficiently names one particular organization. Owning subsidiaries alone does not make a company a family shorthand. FPT Software and Viettel Telecom are specific examples; FPT and Viettel are family-shorthand examples.
Return NameCollision for unrelated plausible matches, and Unknown when you do not safely recognize the organization. Requested hints must be enum values, not prose. Domains and legal names are optional navigation hints, never profile evidence. Keep descriptions short; no hidden reasoning.
Do not infer geography solely from a generic name. When a country or region could materially disambiguate a generic name, return NameCollision or Unknown and request Country rather than choosing a country. Do not manufacture a SpecificEntity by merely repeating the user's query: if you lack independently recognized identifying context such as a reliable country, domain, legal name, or established relationship, return Unknown and request a useful hint.
Return only the structured schema.
Name: {name}
Country: {country ?? "null"}
""";

static JsonElement CreateSchema()
{
    using var doc = JsonDocument.Parse("""
    {"type":"object","properties":{"interpretation":{"type":"string","enum":["SpecificEntity","CorporateFamilyShorthand","NameCollision","Unknown"]},"candidates":{"type":"array","maxItems":7,"items":{"type":"object","properties":{"temporaryId":{"type":"string"},"displayName":{"type":"string"},"country":{"type":"string","nullable":true},"officialDomain":{"type":"string","nullable":true},"entityType":{"type":"string","enum":["ParentGroup","Company","Subsidiary","Affiliate","Brand","Unknown"]},"parentTemporaryId":{"type":"string","nullable":true},"relationshipToQuery":{"type":"string","enum":["Exact","Alias","Parent","Subsidiary","SimilarName","Possible"]},"confidence":{"type":"string","enum":["High","Medium","Low"]}}}},"requestedHints":{"type":"array","maxItems":3,"items":{"type":"string","enum":["Country","Website","LegalName","RegistrationNumber","Headquarters"]}},"message":{"type":"string","nullable":true}},"required":["interpretation","candidates","requestedHints"]}
    """);
    return doc.RootElement.Clone();
}

static bool IsInterpretation(string? value) => value is "SpecificEntity" or "CorporateFamilyShorthand" or "NameCollision" or "Unknown";
static bool IsHint(string? value) => value is "Country" or "Website" or "LegalName" or "RegistrationNumber" or "Headquarters";
static string Bound(string? value) => string.IsNullOrWhiteSpace(value) ? "n/a" : value.Trim()[..Math.Min(value.Trim().Length, 160)];

sealed class Topology
{
    public string? Interpretation { get; set; }
    public List<TopologyCandidate>? Candidates { get; set; }
    public List<string>? RequestedHints { get; set; }
}
sealed class TopologyCandidate
{
    public string? DisplayName { get; set; }
    public string? Country { get; set; }
    public string? OfficialDomain { get; set; }
    public string? EntityType { get; set; }
    public string? ParentTemporaryId { get; set; }
    public string? RelationshipToQuery { get; set; }
    public string? Confidence { get; set; }
}
