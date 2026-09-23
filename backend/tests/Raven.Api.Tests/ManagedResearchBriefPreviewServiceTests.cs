using System.Text.Json;
using Raven.Api.Features.Ai;
using Raven.Api.Features.ManagedResearch;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Settings;

namespace Raven.Api.Tests;

public sealed class ManagedResearchBriefPreviewServiceTests
{
    private static readonly Guid CompanyId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Preview_rewrites_all_requested_topics_into_one_question_without_starting_research()
    {
        var provider = new FakeAiProvider(Json("""
            {"question":"Các nguồn công khai cho biết gì về lãnh đạo và thị trường của FPT Software?"}
            """));
        var service = CreateService(provider);

        var preview = await service.PreviewAsync(CompanyId, new ManagedResearchBriefPreviewRequest("Tìm hiểu lãnh đạo và thị trường"));

        Assert.Equal("Các nguồn công khai cho biết gì về lãnh đạo và thị trường của FPT Software?", preview.Question);
        Assert.Contains("Tìm hiểu lãnh đạo và thị trường", provider.LastRequest?.Prompt, StringComparison.Ordinal);
        Assert.Contains("Preserve every topic", provider.LastRequest?.Prompt, StringComparison.Ordinal);
        Assert.Equal("gemini-3.8-flash", provider.LastRequest?.Model);
        Assert.DoesNotContain("Investigations", provider.LastRequest?.Evidence.IdentityHints?.Keys ?? []);
        Assert.True(ManagedResearchBriefContext.MatchesContextRevision(Context(), preview.ContextRevision));
    }

    [Fact]
    public async Task Preview_rejects_failed_or_invalid_model_rewrites_instead_of_echoing_the_question()
    {
        var provider = new FakeAiProvider(Json("""{"question":" "}"""));
        var service = CreateService(provider);

        await Assert.ThrowsAsync<ManagedResearchBriefGenerationException>(() => service.PreviewAsync(CompanyId, new ManagedResearchBriefPreviewRequest("Research products")));

        provider.Response = null;
        await Assert.ThrowsAsync<ManagedResearchBriefGenerationException>(() => service.PreviewAsync(CompanyId, new ManagedResearchBriefPreviewRequest("Research products")));
    }

    [Fact]
    public async Task Preview_retries_one_transient_failure_with_the_same_request()
    {
        var provider = new FakeAiProvider(Json("""{"question":"Masan Group kinh doanh theo mô hình nào?"}"""))
        {
            Failure = new AiFailure("unavailable", "Gemini is currently unavailable.", true, 503),
            FailuresRemaining = 1
        };

        var preview = await CreateService(provider).PreviewAsync(
            CompanyId, new ManagedResearchBriefPreviewRequest("Mô hình kinh doanh của Masan Group"));

        Assert.Equal("Masan Group kinh doanh theo mô hình nào?", preview.Question);
        Assert.Equal(2, provider.Calls);
        Assert.Same(provider.FirstRequest, provider.LastRequest);
    }

    [Fact]
    public async Task Preview_reports_provider_unavailability_after_one_retry()
    {
        var provider = new FakeAiProvider(null)
        {
            Failure = new AiFailure("unavailable", "Gemini is currently unavailable.", true, 503),
            FailuresRemaining = 2
        };

        var exception = await Assert.ThrowsAsync<ManagedResearchBriefGenerationException>(() =>
            CreateService(provider).PreviewAsync(CompanyId, new ManagedResearchBriefPreviewRequest("Research products")));

        Assert.Equal("unavailable", exception.Code);
        Assert.Contains("Deep Research has not started", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Preview_does_not_retry_nontransient_failure()
    {
        var provider = new FakeAiProvider(null)
        {
            Failure = new AiFailure("authentication", "Gemini rejected the API key.", false, 403),
            FailuresRemaining = 1
        };

        var exception = await Assert.ThrowsAsync<ManagedResearchBriefGenerationException>(() =>
            CreateService(provider).PreviewAsync(CompanyId, new ManagedResearchBriefPreviewRequest("Research products")));

        Assert.Equal("authentication", exception.Code);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Preview_rejects_empty_or_oversized_questions()
    {
        var provider = new FakeAiProvider(Json("""{"question":"Which products are publicly documented?"}"""));
        var service = CreateService(provider);

        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(CompanyId, new ManagedResearchBriefPreviewRequest(" ")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(CompanyId, new ManagedResearchBriefPreviewRequest(new string('a', ManagedResearchLimits.MaxObjectiveLength + 1))));
        provider.Response = Json($"{{\"question\":\"{new string('a', ManagedResearchLimits.MaxObjectiveLength + 1)}\"}}");
        await Assert.ThrowsAsync<ManagedResearchBriefGenerationException>(() => service.PreviewAsync(CompanyId, new ManagedResearchBriefPreviewRequest("Research products")));
    }

    private static ManagedResearchBriefPreviewService CreateService(FakeAiProvider provider) => new(
        new InMemoryManagedResearchCompanyContextReader([Context()]), provider, new FixedSettings());

    private static ManagedResearchCompanyContext Context() => new()
    {
        CompanyId = CompanyId,
        DisplayName = "FPT Software",
        LegalName = "FPT Software Company Limited",
        OfficialWebsite = "https://fptsoftware.com",
        Country = "Vietnam",
        Headquarters = "Hanoi",
        AcceptedProfileSummary = "Technology services company.",
        EvidenceGaps = ["Leadership"]
    };

    private static JsonElement Json(string content)
    {
        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private sealed class FixedSettings : IResearchSettingsService
    {
        private readonly ResearchSettingsResponse response = new(
            GroundingMode.Auto, "gemini-3.5-flash-lite", "gemini-3.5-flash-lite", "gemini-3.5-flash-lite", true,
            ProviderPreset.Custom, ["brave"], ["crawl4ai-local"], DateTimeOffset.UtcNow,
            ManagedResearchProvider: "exa-agent", ManagedResearchDepth: ManagedResearchDepth.Standard,
            ChatModel: "gemini-3.8-flash");

        public Task<ResearchSettingsResponse> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(response);
        public Task<ResearchSettingsResponse> UpdateAsync(UpdateResearchSettingsRequest request, CancellationToken cancellationToken = default) => Task.FromResult(response);
        public Task<ResearchSettingsResponse> ResetAsync(CancellationToken cancellationToken = default) => Task.FromResult(response);
    }

    private sealed class FakeAiProvider(JsonElement? response) : IAiModelProvider
    {
        public string Id => "fake-gemini";
        public JsonElement? Response { get; set; } = response;
        public AiFailure? Failure { get; set; }
        public int FailuresRemaining { get; set; }
        public int Calls { get; private set; }
        public AiModelRequest? FirstRequest { get; private set; }
        public AiModelRequest? LastRequest { get; private set; }

        public Task<AiModelResult> GenerateStructuredAsync(AiModelRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            FirstRequest ??= request;
            LastRequest = request;
            if (FailuresRemaining-- > 0)
                return Task.FromResult(new AiModelResult(Id, request.Model, null, TimeSpan.Zero, Failure: Failure));
            return Task.FromResult(new AiModelResult(Id, request.Model, Response, TimeSpan.Zero));
        }
    }
}
