using System.Text.Json;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Tests;

public sealed class SourceSemanticRerankerTests
{
    private static readonly Guid OfficialCandidateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RelatedCandidateId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ForeignCandidateId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UnknownCandidateId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Same_entity_source_is_promoted_by_structured_assessment()
    {
        var provider = new FakeAiProvider(Response($$"""
            {
              "assessments": [
                {
                  "candidateId": "{{OfficialCandidateId:D}}",
                  "entityRelationship": "SameEntity",
                  "relevance": "High",
                  "recommended": true,
                  "purposes": ["identity", "company_profile"],
                  "rationale": "Official company domain and profile page."
                },
                {
                  "candidateId": "{{RelatedCandidateId:D}}",
                  "entityRelationship": "Subsidiary",
                  "relevance": "Medium",
                  "recommended": false,
                  "purposes": ["supporting_context"],
                  "rationale": "Related entity, not the selected target."
                }
              ]
            }
            """));

        var result = await CreateService(provider).RerankAsync(Request(
            Candidate(OfficialCandidateId, "https://fpt.com", SourceKind.OfficialWebsite),
            Candidate(RelatedCandidateId, "https://software.fpt.com", SourceKind.ExternalWebsite)));

        Assert.Null(result.Failure);
        Assert.Null(result.Warning);
        var assessment = Assert.Single(result.Assessments, item => item.CandidateId == OfficialCandidateId);
        Assert.Equal(EntityRelationship.SameEntity, assessment.EntityRelationship);
        Assert.Equal(CandidateRelevance.High, assessment.Relevance);
        Assert.True(assessment.Recommended);
        Assert.Contains("identity", assessment.Purposes);
        Assert.NotNull(provider.LastRequest);
        Assert.Equal(GeminiSourceSemanticReranker.SourceSemanticPromptTemplateVersion, provider.LastRequest!.PromptTemplateVersion);
    }

    [Fact]
    public async Task Different_entity_is_demoted_and_not_recommended()
    {
        var provider = new FakeAiProvider(Response($$"""
            {
              "assessments": [
                {
                  "candidateId": "{{ForeignCandidateId:D}}",
                  "entityRelationship": "DifferentEntity",
                  "relevance": "Low",
                  "recommended": false,
                  "purposes": ["supporting_context"],
                  "rationale": "Country and domain identify a different company."
                }
              ]
            }
            """));

        var result = await CreateService(provider).RerankAsync(Request(
            Candidate(ForeignCandidateId, "https://sunproperty.example", SourceKind.ExternalWebsite)));

        var assessment = Assert.Single(result.Assessments);
        Assert.Equal(EntityRelationship.DifferentEntity, assessment.EntityRelationship);
        Assert.Equal(CandidateRelevance.Low, assessment.Relevance);
        Assert.False(assessment.Recommended);
    }

    [Fact]
    public async Task Unknown_and_duplicate_candidate_assessments_are_dropped_with_warning()
    {
        var provider = new FakeAiProvider(Response($$"""
            {
              "assessments": [
                {
                  "candidateId": "{{UnknownCandidateId:D}}",
                  "entityRelationship": "SameEntity",
                  "relevance": "High",
                  "recommended": true,
                  "purposes": ["identity"],
                  "rationale": "Unknown candidate must not escape request scope."
                },
                {
                  "candidateId": "{{OfficialCandidateId:D}}",
                  "entityRelationship": "SameEntity",
                  "relevance": "High",
                  "recommended": true,
                  "purposes": ["identity"],
                  "rationale": "Keep the first valid assessment."
                },
                {
                  "candidateId": "{{OfficialCandidateId:D}}",
                  "entityRelationship": "SameEntity",
                  "relevance": "High",
                  "recommended": true,
                  "purposes": ["identity"],
                  "rationale": "Duplicate must be ignored."
                },
                {
                  "candidateId": "not-a-guid",
                  "entityRelationship": "SameEntity",
                  "relevance": "High",
                  "recommended": true,
                  "purposes": ["identity"],
                  "rationale": "Malformed ID must be ignored."
                }
              ]
            }
            """));

        var result = await CreateService(provider).RerankAsync(Request(
            Candidate(OfficialCandidateId, "https://fpt.com", SourceKind.OfficialWebsite)));

        Assert.Single(result.Assessments);
        Assert.NotNull(result.Warning);
        Assert.Contains("unknown candidate", result.Warning!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate", result.Warning!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid candidate ID", result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Model_failure_returns_safe_empty_result_and_failure()
    {
        var provider = new FakeAiProvider(null, new AiFailure(
            "unavailable",
            "The source recommendation model is unavailable.",
            true));

        var result = await CreateService(provider).RerankAsync(Request(
            Candidate(OfficialCandidateId, "https://fpt.com", SourceKind.OfficialWebsite)));

        Assert.Empty(result.Assessments);
        Assert.NotNull(result.Failure);
        Assert.Equal("unavailable", result.Failure!.Code);
        Assert.Equal(result.Failure.Message, result.Warning);
    }

    [Fact]
    public async Task Malformed_model_payload_returns_invalid_response_failure()
    {
        var provider = new FakeAiProvider(JsonDocument.Parse("[\"not an object\"]").RootElement.Clone());

        var result = await CreateService(provider).RerankAsync(Request(
            Candidate(OfficialCandidateId, "https://fpt.com", SourceKind.OfficialWebsite)));

        Assert.Empty(result.Assessments);
        Assert.Equal("invalid_response", result.Failure!.Code);
        Assert.Contains("malformed", result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    private static GeminiSourceSemanticReranker CreateService(FakeAiProvider provider) =>
        new(provider, "gemini-test");

    private static SourceSemanticRerankRequest Request(params SourceSemanticCandidate[] candidates) =>
        new(
            new ResolvedResearchEntity(
                "target",
                "FPT Corporation",
                "FPT Corporation",
                "Vietnam",
                "https://fpt.com",
                "fpt.com",
                GroundedEntityType.ParentGroup,
                null,
                GroundingConfidence.High,
                "Selected target",
                [],
                true),
            candidates);

    private static SourceSemanticCandidate Candidate(Guid id, string url, SourceKind sourceKind) =>
        new(id, url, new Uri(url).Host, "Candidate", "Company information", sourceKind, 75, ["deterministic reason"]);

    private static JsonElement Response(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FakeAiProvider(JsonElement? response = null, AiFailure? failure = null) : IAiModelProvider
    {
        private readonly JsonElement? response = response;
        private readonly AiFailure? failure = failure;

        public string Id => "gemini";
        public AiModelRequest? LastRequest { get; private set; }

        public Task<AiModelResult> GenerateStructuredAsync(
            AiModelRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new AiModelResult(
                Id,
                request.Model,
                response,
                TimeSpan.FromMilliseconds(3),
                Failure: failure));
        }
    }
}
