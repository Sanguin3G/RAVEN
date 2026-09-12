using Raven.Api.Features.Research.Identity;

namespace Raven.Api.Tests;

public sealed class IdentityResolutionServiceTests
{
    [Fact]
    public async Task Exact_name_confirmation_resolves_without_a_knowledge_call()
    {
        var knowledge = new SpyKnowledgeResolver(_ => throw new InvalidOperationException("must not call"));
        var service = new IdentityResolutionService(knowledge, new IdentityResolutionPolicy());

        var result = await service.ResolveAsync(new IdentityResolutionRequest(
            "  obscure firm  ",
            Website: "https://www.obscure.example/path",
            Country: "Vietnam",
            ConfirmExactName: true));

        Assert.True(result.IsValid);
        Assert.Equal(IdentityResolutionStatus.Resolved, result.Response!.Status);
        Assert.Equal(IdentityResolutionMethod.UserConfirmedExactInput, result.Response.ResolutionMethod);
        Assert.Equal("user-exact", result.Response.RecommendedEntityId);
        Assert.Equal("obscure firm", result.Response.Entities.Single().DisplayName);
        Assert.Equal("obscure.example", result.Response.Entities.Single().OfficialDomain);
        Assert.Equal(0, knowledge.CallCount);
    }

    [Fact]
    public async Task Model_off_requests_a_stronger_hint_without_a_knowledge_call()
    {
        var knowledge = new SpyKnowledgeResolver(_ => throw new InvalidOperationException("must not call"));
        var service = new IdentityResolutionService(knowledge, new IdentityResolutionPolicy());

        var result = await service.ResolveAsync(new IdentityResolutionRequest("FPT", AllowModelKnowledge: false));

        Assert.Equal(IdentityResolutionStatus.NeedsMoreInfo, result.Response!.Status);
        Assert.Contains(IdentityHintKind.Website, result.Response.RequestedHints);
        Assert.Contains(IdentityHintKind.Country, result.Response.RequestedHints);
        Assert.Equal(0, knowledge.CallCount);
    }

    [Fact]
    public async Task Guided_refinement_is_an_explicit_knowledge_call()
    {
        IdentityResolutionRequest? received = null;
        var knowledge = new SpyKnowledgeResolver(request =>
        {
            received = request;
            return new IdentityTopologyResponse(IdentityQueryInterpretation.Unknown, [], [IdentityHintKind.ResearchHint]);
        });
        var service = new IdentityResolutionService(knowledge, new IdentityResolutionPolicy());

        var result = await service.ResolveAsync(new IdentityResolutionRequest(
            "FPT",
            Website: "https://fpt.com.vn",
            GuidedRefinement: true));

        Assert.Equal(IdentityResolutionStatus.NeedsMoreInfo, result.Response!.Status);
        Assert.Empty(result.Response.Entities);
        Assert.Null(result.Response.RecommendedEntityId);
        Assert.True(received?.GuidedRefinement);
        Assert.Equal(1, knowledge.CallCount);
    }

    [Fact]
    public async Task Specific_entity_with_one_high_confidence_exact_candidate_resolves()
    {
        var knowledge = new SpyKnowledgeResolver(_ => new IdentityTopologyResponse(
            IdentityQueryInterpretation.SpecificEntity,
            [Option("fpt-software", "FPT Software", IdentityRelationshipToQuery.Exact, IdentityConfidence.High)],
            []));
        var service = new IdentityResolutionService(knowledge, new IdentityResolutionPolicy());

        var result = await service.ResolveAsync(new IdentityResolutionRequest("FPT Software"));

        Assert.True(result.IsValid);
        Assert.Equal(IdentityResolutionStatus.Resolved, result.Response!.Status);
        Assert.Equal("fpt-software", result.Response.RecommendedEntityId);
        Assert.Equal(1, knowledge.CallCount);
    }

    [Fact]
    public async Task Generic_short_name_cannot_auto_resolve_to_a_parent_group()
    {
        var knowledge = new SpyKnowledgeResolver(_ => new IdentityTopologyResponse(
            IdentityQueryInterpretation.SpecificEntity,
            [Option("viettel-group", "Viettel Group", IdentityRelationshipToQuery.Exact, IdentityConfidence.High, IdentityEntityType.ParentGroup)],
            []));
        var service = new IdentityResolutionService(knowledge, new IdentityResolutionPolicy());

        var result = await service.ResolveAsync(new IdentityResolutionRequest("Viettel"));

        Assert.Equal(IdentityResolutionStatus.NeedsMoreInfo, result.Response!.Status);
        Assert.Null(result.Response.RecommendedEntityId);
        Assert.Equal(1, knowledge.CallCount);
    }

    [Fact]
    public async Task Corporate_family_topology_is_ambiguous_even_when_parent_is_first()
    {
        var knowledge = new SpyKnowledgeResolver(_ => new IdentityTopologyResponse(
            IdentityQueryInterpretation.CorporateFamilyShorthand,
            [
                Option("fpt", "FPT Corporation", IdentityRelationshipToQuery.Exact, IdentityConfidence.High, IdentityEntityType.ParentGroup),
                Option("fpt-software", "FPT Software", IdentityRelationshipToQuery.Subsidiary, IdentityConfidence.High, IdentityEntityType.Subsidiary,
                    parentTemporaryId: "fpt")
            ],
            []));
        var service = new IdentityResolutionService(knowledge, new IdentityResolutionPolicy());

        var result = await service.ResolveAsync(new IdentityResolutionRequest("FPT"));

        Assert.True(result.IsValid);
        Assert.Equal(IdentityResolutionStatus.Ambiguous, result.Response!.Status);
        Assert.Equal(IdentityAmbiguityType.CorporateFamily, result.Response.AmbiguityType);
        Assert.Null(result.Response.RecommendedEntityId);
        Assert.Equal("fpt", result.Response.Entities[0].TemporaryId);
        Assert.Equal(1, knowledge.CallCount);
    }

    [Fact]
    public async Task Name_collision_requires_multiple_meaningful_candidates()
    {
        var knowledge = new SpyKnowledgeResolver(_ => new IdentityTopologyResponse(
            IdentityQueryInterpretation.NameCollision,
            [
                Option("sun-vn", "Sun Property Vietnam", IdentityRelationshipToQuery.SimilarName, IdentityConfidence.High),
                Option("sun-us", "Sun Property Massachusetts", IdentityRelationshipToQuery.SimilarName, IdentityConfidence.High)
            ],
            [IdentityHintKind.Country]));
        var service = new IdentityResolutionService(knowledge, new IdentityResolutionPolicy());

        var result = await service.ResolveAsync(new IdentityResolutionRequest("Sun Property"));

        Assert.Equal(IdentityResolutionStatus.Ambiguous, result.Response!.Status);
        Assert.Equal(IdentityAmbiguityType.NameCollision, result.Response.AmbiguityType);
        Assert.Null(result.Response.RecommendedEntityId);
        Assert.Equal(1, knowledge.CallCount);
    }

    [Fact]
    public async Task Resolver_failure_becomes_safe_clarification_instead_of_resolution()
    {
        var knowledge = new SpyKnowledgeResolver(_ => throw new InvalidOperationException("provider details must not leak"));
        var service = new IdentityResolutionService(knowledge, new IdentityResolutionPolicy());

        var result = await service.ResolveAsync(new IdentityResolutionRequest("Unknown firm"));

        Assert.Equal(IdentityResolutionStatus.NeedsMoreInfo, result.Response!.Status);
        Assert.Null(result.Response.RecommendedEntityId);
        Assert.DoesNotContain("provider details", result.Response.Message ?? string.Empty);
        Assert.Equal(1, knowledge.CallCount);
    }

    [Fact]
    public async Task Invalid_request_does_not_call_knowledge_resolver()
    {
        var knowledge = new SpyKnowledgeResolver(_ => throw new InvalidOperationException("must not call"));
        var service = new IdentityResolutionService(knowledge, new IdentityResolutionPolicy());

        var result = await service.ResolveAsync(new IdentityResolutionRequest(" "));

        Assert.False(result.IsValid);
        Assert.Null(result.Response);
        Assert.Contains(nameof(IdentityResolutionRequest.Name), result.ValidationErrors.Keys);
        Assert.Equal(0, knowledge.CallCount);
    }

    private static IdentityOption Option(
        string id,
        string name,
        IdentityRelationshipToQuery relationship,
        IdentityConfidence confidence,
        IdentityEntityType entityType = IdentityEntityType.Company,
        string? parentTemporaryId = null) => new(
        id,
        name,
        null,
        "Vietnam",
        null,
        null,
        entityType,
        parentTemporaryId,
        relationship,
        confidence,
        null);

    private sealed class SpyKnowledgeResolver(
        Func<IdentityResolutionRequest, IdentityTopologyResponse> resolver) : IIdentityKnowledgeResolver
    {
        public int CallCount { get; private set; }

        public Task<IdentityTopologyResponse> ResolveAsync(
            IdentityResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(resolver(request));
        }
    }
}
