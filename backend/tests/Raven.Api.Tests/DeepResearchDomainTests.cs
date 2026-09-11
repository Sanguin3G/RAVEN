using System.Text.Json;
using Raven.Api.Features.DeepResearch;

namespace Raven.Api.Tests;

public sealed class DeepResearchDomainTests
{
    [Fact]
    public void Default_budget_is_bounded()
    {
        var budget = DeepResearchBudget.Default;

        Assert.Equal(15, budget.MaxToolCalls);
        Assert.Equal(6, budget.MaxSearchCalls);
        Assert.Equal(10, budget.MaxCrawlCalls);
        Assert.Equal(12, budget.MaxDocuments);
        Assert.Equal(TimeSpan.FromMinutes(5), budget.EffectiveMaxDuration);
    }

    [Fact]
    public async Task Budgeted_toolset_does_not_call_provider_after_search_budget()
    {
        var provider = new FakeToolset();
        var activity = new InMemoryDeepResearchActivityStore();
        var runId = Guid.NewGuid();
        var tools = new DeepResearchBudgetedToolset(
            provider,
            new DeepResearchBudget(MaxToolCalls: 3, MaxSearchCalls: 1, MaxCrawlCalls: 1, MaxDocuments: 2),
            activity,
            runId);

        var first = await tools.SearchWebAsync("first", 5);
        var second = await tools.SearchWebAsync("second", 5);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Contains("search budget", second.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, provider.SearchCalls);
        Assert.Equal(1, tools.Usage.SearchCalls);
        Assert.Equal(1, tools.Usage.ToolCalls);
        Assert.All(await activity.ListAsync(runId), item => Assert.DoesNotContain("raw", item.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Activity_sanitizer_exposes_only_bounded_public_fields()
    {
        var sourceIds = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();
        var eventData = DeepResearchActivitySanitizer.Sanitize(new DeepResearchActivityEvent(
            "not-a-public-type",
            DeepResearchActivityStatus.Completed,
            new string('L', 500),
            new string('D', 500),
            new string('P', 500),
            sourceIds));

        Assert.Equal("tool", eventData.Type);
        Assert.Equal(DeepResearchActivitySanitizer.MaxLabelLength, eventData.Label.Length);
        Assert.Equal(DeepResearchActivitySanitizer.MaxDetailLength, eventData.Detail!.Length);
        Assert.Equal(DeepResearchActivitySanitizer.MaxProviderLength, eventData.Provider!.Length);
        Assert.Equal(DeepResearchActivitySanitizer.MaxSourceDocumentIds, eventData.SourceDocumentIds.Count);

        var propertyNames = JsonSerializer.SerializeToDocument(eventData).RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(
            ["Type", "Status", "Label", "Detail", "Provider", "SourceDocumentIds"],
            propertyNames);
    }

    [Fact]
    public async Task Run_service_persists_completion_and_counters_without_profile_mutation()
    {
        var store = new InMemoryDeepResearchRunStore();
        var agent = new FakeAgent(new DeepResearchAgentResult(
            "## Answer\n\nUnknown.",
            null,
            new DeepResearchUsage(3, 1, 1, 2)));
        var activity = new InMemoryDeepResearchActivityStore();
        var profile = new MutableProfileReadModel();
        var service = new DeepResearchRunService(store, agent, new FakeToolset(profile), activity);
        var companyId = Guid.NewGuid();

        var started = await service.StartAsync(companyId, new StartDeepResearchRequest("What is this company?"));
        var completed = await service.ExecuteAsync(started.Id);

        Assert.NotNull(completed);
        Assert.Equal(DeepResearchRunStatus.Completed, completed!.Status);
        Assert.Equal(3, completed.ToolCalls);
        Assert.Equal(1, completed.SearchCalls);
        Assert.Equal(1, completed.CrawlCalls);
        Assert.Equal(2, completed.DocumentsRead);
        Assert.Equal("original", profile.Value);
        Assert.Contains((await activity.ListAsync(started.Id)), item => item.Label == "Deep Research completed");
    }

    [Fact]
    public async Task Cancelling_queued_run_prevents_agent_execution()
    {
        var agent = new FakeAgent(new DeepResearchAgentResult("answer", null, new DeepResearchUsage(1, 0, 0, 0)));
        var service = new DeepResearchRunService(
            new InMemoryDeepResearchRunStore(),
            agent,
            new FakeToolset());
        var started = await service.StartAsync(Guid.NewGuid(), new StartDeepResearchRequest("cancel me"));

        var cancelled = await service.CancelAsync(started.Id);
        var result = await service.ExecuteAsync(started.Id);

        Assert.Equal(DeepResearchRunStatus.Queued, cancelled!.Status);
        Assert.Equal(DeepResearchRunStatus.Cancelled, result!.Status);
        Assert.False(agent.Called);
    }

    private sealed class FakeAgent(DeepResearchAgentResult result) : IDeepResearchAgent
    {
        public bool Called { get; private set; }

        public Task<DeepResearchAgentResult> RunAsync(DeepResearchAgentRequest request, CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeToolset(MutableProfileReadModel? profile = null) : IDeepResearchToolset
    {
        private readonly MutableProfileReadModel profile = profile ?? new();
        public int SearchCalls { get; private set; }

        public Task<DeepResearchToolResult<DeepResearchProfile>> GetCompanyProfileAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeepResearchToolResult<DeepResearchProfile>(true, new DeepResearchProfile(companyId, 1, "Test", null, null, null, null, null, profile.Value), null));

        public Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>> GetCompanySourcesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>(true, [], null));

        public Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSearchHit>>> SearchWebAsync(string query, int maxResults, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult(new DeepResearchToolResult<IReadOnlyList<DeepResearchSearchHit>>(true, [new DeepResearchSearchHit("Result", "https://example.com", "Snippet", 1)], null, "fake"));
        }

        public Task<DeepResearchToolResult<DeepResearchCrawlPage>> CrawlPageAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeepResearchToolResult<DeepResearchCrawlPage>(false, null, "not used"));

        public Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>> SearchStoredSourceTextAsync(Guid companyId, string query, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>(false, null, "not enabled"));
    }

    private sealed class MutableProfileReadModel
    {
        public string Value { get; set; } = "original";
    }
}
