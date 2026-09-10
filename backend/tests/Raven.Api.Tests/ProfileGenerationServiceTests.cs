using System.Text.Json;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Generation;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Tests;

public sealed class ProfileGenerationServiceTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RunId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondSourceId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Input_builder_orders_and_bounds_source_evidence_without_forwarding_unsafe_urls()
    {
        var builder = new ProfileInputBuilder(
            new SourceAuthorityPolicy(),
            new ProfileInputBuilderOptions
            {
                MaxSources = 3,
                MaxCharactersPerSource = 12,
                MaxTotalCharacters = 20,
                MaxStructuredFactsCharacters = 500
            });

        var package = builder.Build(
            new ProfileIdentityHints(DisplayName: " Example Co ", ResearchHint: "find products"),
            [
                Document(SourceId, "https://example.com/about", SourceKind.OfficialWebsite, "Official", "official content that must be bounded", "{\"Industry\":\"Technology\"}"),
                Document(SecondSourceId, "https://registry.example/company/1", SourceKind.BusinessRegistry, "Registry", "registered identity"),
                Document(Guid.NewGuid(), "javascript:alert(1)", SourceKind.ExternalWebsite, "Unsafe", "do not forward")
            ]);

        Assert.Equal(2, package.Sources.Count);
        Assert.Equal(SecondSourceId, package.Sources[0].SourceDocumentId);
        Assert.All(package.Payload.Sources, source => Assert.StartsWith("https://", source.Url, StringComparison.Ordinal));
        Assert.All(package.Payload.Sources, source => Assert.True(source.Content.Length <= 12));
        Assert.Contains(package.Payload.Sources, source => source.StructuredFacts is not null && source.StructuredFacts["Industry"] == "Technology");
        Assert.Equal("Example Co", package.Payload.IdentityHints!["DisplayName"]);
        Assert.Equal("find products", package.Payload.IdentityHints["ResearchHint"]);
        Assert.True(package.CharacterCount <= 20);
    }

    [Fact]
    public async Task Generation_returns_valid_structured_candidate_and_sends_bounded_evidence()
    {
        var provider = new FakeAiProvider(ProfileJson($$"""
            {
              "displayName": "Example Co",
              "legalName": null,
              "website": "https://example.com",
              "country": "Vietnam",
              "headquarters": null,
              "registrationNumberOrTaxId": null,
              "foundedYear": null,
              "primaryIndustry": "Technology",
              "secondaryIndustries": [],
              "companySize": null,
              "employeeCount": null,
              "employeeCountRange": "201-500",
              "summary": "A supplied-evidence summary.",
              "productsServices": [{"name":"Research","type":"Service","description":null}],
              "markets": [],
              "leadership": [],
              "locations": [],
              "publicLinks": [],
              "evidence": [{"fieldPath":"primaryIndustry","sourceDocumentIds":["{{SourceId:D}}"]}]
            }
            """));
        var service = CreateService(provider);

        var result = await service.GenerateAsync(Input(Document(SourceId, "https://example.com/about", SourceKind.OfficialWebsite, "About", new string('x', 100))));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Candidate);
        Assert.Equal("Example Co", result.Candidate.DisplayName);
        Assert.Equal("201-500", result.Candidate.EmployeeCountRange);
        Assert.Null(result.Candidate.EmployeeCount);
        Assert.Single(result.Candidate.ProductsServices);
        Assert.Equal("primaryIndustry", result.Candidate.Evidence.Single().FieldPath);
        Assert.Equal(SourceId, result.Candidate.Evidence.Single().SourceDocumentIds.Single());
        Assert.Equal("gemini", result.Provider);
        Assert.Equal("gemini-test", result.Model);
        Assert.NotNull(provider.LastRequest);
        Assert.Contains("only the supplied", provider.LastRequest!.SystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SourceId.ToString("D"), provider.LastRequest.Evidence.Sources.Single().SourceId);
        Assert.True(provider.LastRequest.ResponseSchema.ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task Generation_preserves_unknown_as_null_and_empty_collections()
    {
        var provider = new FakeAiProvider(ProfileJson("""
            {
              "displayName": null,
              "legalName": null,
              "website": null,
              "country": null,
              "headquarters": null,
              "registrationNumberOrTaxId": null,
              "foundedYear": null,
              "primaryIndustry": null,
              "secondaryIndustries": [],
              "companySize": null,
              "employeeCount": null,
              "employeeCountRange": null,
              "summary": null,
              "productsServices": [],
              "markets": [],
              "leadership": [],
              "locations": [],
              "publicLinks": [],
              "evidence": []
            }
            """));

        var result = await CreateService(provider).GenerateAsync(Input());

        Assert.True(result.Succeeded);
        Assert.Null(result.Candidate!.Summary);
        Assert.Null(result.Candidate.EmployeeCount);
        Assert.Empty(result.Candidate.ProductsServices);
        Assert.Empty(result.Candidate.Markets);
        Assert.Empty(result.Candidate.Evidence);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Generation_rejects_malformed_profile_shape_without_persisting_a_candidate()
    {
        var provider = new FakeAiProvider(JsonDocument.Parse("[\"not a profile\"]").RootElement.Clone());

        var result = await CreateService(provider).GenerateAsync(Input());

        Assert.False(result.Succeeded);
        Assert.Null(result.Candidate);
        Assert.Equal("invalid_response", result.Failure!.Code);
        Assert.Contains("malformed", result.Warnings.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generation_validates_unknown_foreign_and_duplicate_evidence_references()
    {
        var unknown = Guid.NewGuid();
        var provider = new FakeAiProvider(ProfileJson($$"""
            {
              "displayName": "Example Co",
              "summary": "Supported",
              "evidence": [
                {"fieldPath":"summary","sourceDocumentIds":["{{SourceId:D}}","{{SourceId:D}}","{{unknown:D}}"]},
                {"fieldPath":"summary","sourceDocumentIds":["{{SecondSourceId:D}}"]}
              ]
            }
            """));
        var input = Input(Document(SourceId, "https://example.com/about", SourceKind.OfficialWebsite, "About", "supported"));

        var result = await CreateService(provider).GenerateAsync(input);

        Assert.True(result.Succeeded);
        var evidence = Assert.Single(result.Candidate!.Evidence);
        Assert.Equal("summary", evidence.FieldPath);
        Assert.Equal([SourceId], evidence.SourceDocumentIds);
        Assert.Contains(result.Warnings, warning => warning.Contains(unknown.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains(SecondSourceId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static ProfileGenerationService CreateService(FakeAiProvider provider) =>
        new(
            provider,
            new ProfileInputBuilder(new SourceAuthorityPolicy()),
            new ProfileGenerationOptions { Model = "gemini-test", PromptTemplateVersion = "test-v1" });

    private static ProfileGenerationInput Input(params SourceDocument[] documents) =>
        new(
            CompanyId,
            RunId,
            new ProfileIdentityHints(DisplayName: "Example Co", Country: "Vietnam"),
            documents,
            new ProfileValidationContext(
                CompanyId,
                RunId,
                documents.Select(document => document.Id).ToHashSet(),
                documents.Select(document => document.Id).ToHashSet()));

    private static SourceDocument Document(
        Guid id,
        string url,
        SourceKind sourceKind,
        string title,
        string content,
        string? structuredFactsJson = null) =>
        new()
        {
            Id = id,
            CompanyId = CompanyId,
            ResearchRunId = RunId,
            Url = url,
            NormalizedUrl = url,
            Title = title,
            SourceDomain = new Uri(url, UriKind.RelativeOrAbsolute).Host,
            SourceKind = sourceKind,
            StructuredFactsJson = structuredFactsJson,
            RetrievedAt = DateTimeOffset.UtcNow,
            Content = content,
            ContentHash = "hash",
            CrawlerProvider = "fake"
        };

    private static JsonElement ProfileJson(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FakeAiProvider(JsonElement response) : IAiModelProvider
    {
        public string Id => "gemini";
        public AiModelRequest? LastRequest { get; private set; }

        public Task<AiModelResult> GenerateStructuredAsync(AiModelRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new AiModelResult(Id, request.Model, response, TimeSpan.FromMilliseconds(3)));
        }
    }
}
