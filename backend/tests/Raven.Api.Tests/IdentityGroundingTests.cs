using System.Text.Json;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Tests;

public sealed class IdentityGroundingTests
{
    private static readonly Guid OfficialCandidateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RelatedCandidateId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Auto_skips_grounding_for_a_strongly_identified_company()
    {
        var analyzer = new IdentityAmbiguityAnalyzer();
        var request = new IdentityResolutionRequest(
            new ResearchIdentityInput(
                "FPT Software",
                "FPT Software Company Limited",
                "https://fptsoftware.com",
                "Vietnam",
                "0101248141",
                "Hanoi",
                null),
            [Candidate(OfficialCandidateId, "fptsoftware.com", SourceKind.OfficialWebsite, official: true)]);

        var analysis = analyzer.Analyze(request);

        Assert.False(analysis.ShouldResolve);
        Assert.Empty(analysis.Signals);
        Assert.False(analyzer.ShouldResolve(request, GroundingMode.Auto));
    }

    [Fact]
    public void Auto_requests_grounding_for_short_names_and_conflicting_search_results()
    {
        var analyzer = new IdentityAmbiguityAnalyzer();
        var request = new IdentityResolutionRequest(
            new ResearchIdentityInput("FPT", null, null, "Vietnam", null, null, null),
            [
                Candidate(OfficialCandidateId, "fpt.com", SourceKind.OfficialWebsite, official: true, title: "FPT Corporation"),
                Candidate(RelatedCandidateId, "fptsoftware.com", SourceKind.OfficialWebsite, official: true, title: "FPT Software subsidiary"),
                Candidate(Guid.Parse("33333333-3333-3333-3333-333333333333"), "linkedin.com", SourceKind.LinkedIn, title: "FPT Americas affiliate")
            ]);

        var analysis = analyzer.Analyze(request);

        Assert.True(analysis.ShouldResolve);
        Assert.Contains("company name is short or general", analysis.Signals);
        Assert.Contains("search results span several domains", analysis.Signals);
        Assert.Contains("multiple official-looking domains were found", analysis.Signals);
        Assert.Contains("results mix a parent organization with related entities", analysis.Signals);
        Assert.True(analyzer.ShouldResolve(request, GroundingMode.Always));
        Assert.False(analyzer.ShouldResolve(request, GroundingMode.Off));
    }

    [Fact]
    public async Task Resolver_maps_valid_structured_entities_and_candidate_references()
    {
        var provider = new FakeAiModelProvider(Json("""
            {
              "ambiguous": true,
              "recommendedTemporaryId": "entity-fpt",
              "entities": [
                {
                  "temporaryId": "entity-fpt",
                  "displayName": "FPT Corporation",
                  "legalName": "FPT Corporation",
                  "country": "Vietnam",
                  "website": "https://fpt.com",
                  "officialDomain": "fpt.com",
                  "entityType": "parent_group",
                  "relationshipHint": "Selected parent group",
                  "confidence": "high",
                  "rationale": "The official group domain matches the Vietnam hint.",
                  "supportingCandidateIds": ["11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111"],
                  "recommended": true
                }
              ]
            }
            """));
        var resolver = new GeminiCompanyIdentityResolver(provider);

        var result = await resolver.ResolveAsync(RequestWithCandidates());

        Assert.Null(result.Failure);
        Assert.True(result.Ambiguous);
        Assert.Equal("entity-fpt", result.RecommendedTemporaryId);
        var entity = Assert.Single(result.Entities);
        Assert.Equal(GroundedEntityType.ParentGroup, entity.EntityType);
        Assert.Equal(GroundingConfidence.High, entity.Confidence);
        Assert.Equal([OfficialCandidateId], entity.SupportingCandidateIds);
        Assert.Equal("gemini-3.5-flash-lite", provider.LastRequest!.Model);
        Assert.Contains(OfficialCandidateId.ToString("D"), provider.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.Equal("company-identity-grounding-v1", provider.LastRequest.PromptTemplateVersion);
    }

    [Fact]
    public async Task Resolver_returns_sanitized_failure_for_malformed_structured_output()
    {
        var resolver = new GeminiCompanyIdentityResolver(
            new FakeAiModelProvider(Json("[]")));

        var result = await resolver.ResolveAsync(RequestWithCandidates());

        Assert.NotNull(result.Failure);
        Assert.Equal("invalid_response", result.Failure!.Code);
        Assert.NotNull(result.Warning);
        Assert.Empty(result.Entities);
    }

    [Fact]
    public async Task Resolver_rejects_unknown_supporting_candidate_ids()
    {
        var provider = new FakeAiModelProvider(Json("""
            {
              "ambiguous": true,
              "entities": [{
                "temporaryId": "entity-1",
                "displayName": "FPT",
                "entityType": "company",
                "confidence": "medium",
                "supportingCandidateIds": ["99999999-9999-9999-9999-999999999999"],
                "recommended": true
              }]
            }
            """));
        var resolver = new GeminiCompanyIdentityResolver(provider);

        var result = await resolver.ResolveAsync(RequestWithCandidates());

        Assert.Equal("invalid_candidate_reference", result.Failure!.Code);
        Assert.Contains("unknown source reference", result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Entities);
    }

    [Fact]
    public async Task Resolver_returns_provider_failure_for_unavailable_model()
    {
        var provider = new FakeAiModelProvider(
            null,
            new AiFailure("unavailable", "Gemini is currently unavailable.", true));
        var resolver = new GeminiCompanyIdentityResolver(provider);

        var result = await resolver.ResolveAsync(RequestWithCandidates());

        Assert.Equal("unavailable", result.Failure!.Code);
        Assert.True(result.Failure.Retryable);
        Assert.NotNull(result.Warning);
        Assert.Empty(result.Entities);
    }

    [Fact]
    public async Task Resolver_converts_provider_exception_to_fallback_failure()
    {
        var resolver = new GeminiCompanyIdentityResolver(
            new FakeAiModelProvider(throwOnGenerate: true));

        var result = await resolver.ResolveAsync(RequestWithCandidates());

        Assert.Equal("provider_error", result.Failure!.Code);
        Assert.NotNull(result.Warning);
        Assert.Empty(result.Entities);
    }

    private static IdentityResolutionRequest RequestWithCandidates() => new(
        new ResearchIdentityInput("FPT", null, null, "Vietnam", null, null, null),
        [Candidate(OfficialCandidateId, "fpt.com", SourceKind.OfficialWebsite, official: true)]);

    private static GroundingSourceCandidate Candidate(
        Guid id,
        string domain,
        SourceKind sourceKind,
        bool official = false,
        string? title = null) => new(
        id,
        $"https://{domain}/about",
        domain,
        title ?? domain,
        "Company discovery snippet",
        sourceKind,
        1,
        ["official company domain"],
        official);

    private static JsonElement Json(string content)
    {
        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private sealed class FakeAiModelProvider(
        JsonElement? structuredJson = null,
        AiFailure? failure = null,
        bool throwOnGenerate = false) : IAiModelProvider
    {
        public string Id => "fake-gemini";

        public AiModelRequest? LastRequest { get; private set; }

        public Task<AiModelResult> GenerateStructuredAsync(
            AiModelRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (throwOnGenerate)
            {
                throw new InvalidOperationException("simulated provider failure");
            }

            return Task.FromResult(new AiModelResult(
                "fake-gemini",
                request.Model,
                structuredJson,
                TimeSpan.Zero,
                Failure: failure));
        }
    }
}
