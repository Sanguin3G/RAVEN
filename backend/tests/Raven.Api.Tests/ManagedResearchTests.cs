using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Api.Features.ManagedResearch;

namespace Raven.Api.Tests;

public sealed class ManagedResearchTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Exa_agent_client_uses_bearer_auth_and_agent_run_lifecycle_paths()
    {
        var requests = new List<HttpRequestMessage>();
        string? createBody = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request);
            if (request.Method == HttpMethod.Post && !request.RequestUri!.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal))
            {
                createBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            return request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal)
                ? Json("{\"id\":\"agent_run_1\",\"status\":\"cancelled\"}")
                : request.Method == HttpMethod.Post
                    ? Json("{\"agent_run\":{\"id\":\"agent_run_1\",\"status\":\"queued\"}}")
                    : Json("{\"id\":\"agent_run_1\",\"status\":\"running\"}");
        })) { BaseAddress = new Uri("https://api.exa.ai") };
        var clientAdapter = new ExaAgentClient(
            client,
            Options.Create(new ExaAgentOptions { ApiKey = "exa-test-key" }));

        var created = await clientAdapter.CreateAsync(new ManagedResearchAgentCreateRequest("Research FPT", ManagedResearchEffort.High));
        var polled = await clientAdapter.GetAsync(created.Id);
        var cancelled = await clientAdapter.CancelAsync(created.Id);

        Assert.Equal("agent_run_1", created.Id);
        Assert.Equal(ManagedResearchProviderRunStatus.Queued, created.Status);
        Assert.Equal(ManagedResearchProviderRunStatus.Running, polled.Status);
        Assert.Equal(ManagedResearchProviderRunStatus.Cancelled, cancelled.Status);
        Assert.Equal(3, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("exa-test-key", request.Headers.Authorization?.Parameter);
            Assert.False(request.Headers.Contains("Exa-Beta"));
        });
        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.Equal("/agent/runs", requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, requests[1].Method);
        Assert.Equal("/agent/runs/agent_run_1", requests[1].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, requests[2].Method);
        Assert.Equal("/agent/runs/agent_run_1/cancel", requests[2].RequestUri!.AbsolutePath);

        using var body = JsonDocument.Parse(createBody!);
        Assert.Equal("Research FPT", body.RootElement.GetProperty("query").GetString());
        Assert.Equal("high", body.RootElement.GetProperty("effort").GetString());
        Assert.Equal("object", body.RootElement.GetProperty("outputSchema").GetProperty("type").GetString());
    }

    [Fact]
    public void Normalizer_preserves_structured_claim_urls_grounding_and_safe_cost_metadata()
    {
        using var document = JsonDocument.Parse("""
            {
              "summary": "The company serves enterprise customers.",
              "claims": [
                {
                  "topic": "Markets",
                  "statement": "It serves enterprise customers.",
                  "supportingSourceUrls": ["https://example.com/about"],
                  "providerConfidence": 1.2,
                  "notes": "Provider confidence is advisory."
                }
              ],
              "sources": [
                {"title":"About","url":"https://example.com/about","publisher":"Example"}
              ],
              "uncertainties": ["Employee scale was not found."]
            }
            """);

        var result = ManagedResearchResultNormalizer.Normalize(
            "Research markets",
            new ManagedResearchProviderRun(
                "agent_run_1",
                ManagedResearchProviderRunStatus.Completed,
                ExaAgentClient.ProviderId,
                new ManagedResearchProviderOutput(
                    "The company serves enterprise customers.",
                    document.RootElement.Clone(),
                    [new ManagedResearchCitation("https://example.com/lead", "Lead", "claims[0]")] ),
                CompletedAt: Now,
                CostDollars: 0.12m,
                StopReason: "schema_satisfied"),
            Now);

        var claim = Assert.Single(result.Claims);
        Assert.Equal("Markets", claim.Topic);
        Assert.Equal(1m, claim.ProviderConfidence);
        Assert.Contains("https://example.com/about", claim.SupportingSourceUrls);
        Assert.Equal(2, result.Sources.Count);
        Assert.Contains(result.Sources, source => source.Url == "https://example.com/lead");
        Assert.Equal("0.12", result.SafeMetadata["costDollars"]);
        Assert.Equal("schema_satisfied", result.SafeMetadata["stopReason"]);
        Assert.Equal("Employee scale was not found.", Assert.Single(result.Uncertainties));
    }

    [Fact]
    public async Task Job_service_queues_contextual_job_and_saves_completed_investigation_without_profile_writer()
    {
        var queue = new RecordingQueue();
        var jobs = new InMemoryManagedResearchJobStore();
        var investigations = new InMemoryManagedResearchInvestigationStore();
        var provider = new FakeAgentClient();
        var service = CreateService(jobs, investigations, provider, queue);

        const string question = "Which leaders and markets are publicly documented for FPT Software?";
        var started = await service.StartAsync(CompanyId, new StartManagedResearchRequest(
            question,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ManagedResearchEffort.Medium));

        Assert.Equal(ManagedResearchJobStatus.Queued, started.Status);
        Assert.Equal(CompanyId, started.CompanyId);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), started.ConversationId);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), started.ChatMessageId);
        Assert.Equal(question, started.Objective);
        Assert.Contains(queue.JobIds, id => id == started.Id);

        var completed = await service.ProcessAsync(started.Id);

        Assert.NotNull(completed);
        Assert.Equal(ManagedResearchJobStatus.Completed, completed!.Status);
        Assert.NotEqual(Guid.Empty, completed.InvestigationId);
        var saved = Assert.Single(await investigations.ListForCompanyAsync(CompanyId));
        Assert.Equal(started.Id, saved.JobId);
        Assert.Equal("ManagedAi", saved.Origin);
        Assert.Contains("enterprise customers", saved.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(completed.Result);
        Assert.Single(completed.Result!.Claims);
        Assert.Contains("FPT Software", provider.LastCreateRequest?.Query, StringComparison.Ordinal);
        Assert.Contains("EmployeeScale", provider.LastCreateRequest?.Query, StringComparison.Ordinal);
        Assert.Contains("Research question:", provider.LastCreateRequest?.Query, StringComparison.Ordinal);
        Assert.Contains(question, provider.LastCreateRequest?.Query, StringComparison.Ordinal);
        Assert.Contains("Use authoritative public sources", provider.LastCreateRequest?.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("Investigation brief:", provider.LastCreateRequest?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Queued_job_cancellation_is_durable_and_does_not_call_provider()
    {
        var queue = new RecordingQueue();
        var provider = new FakeAgentClient();
        var service = CreateService(
            new InMemoryManagedResearchJobStore(),
            new InMemoryManagedResearchInvestigationStore(),
            provider,
            queue);

        var started = await service.StartAsync(CompanyId, new StartManagedResearchRequest("Research products"));
        var cancelled = await service.CancelAsync(CompanyId, started.Id);

        Assert.NotNull(cancelled);
        Assert.Equal(ManagedResearchJobStatus.Cancelled, cancelled!.Status);
        Assert.Equal(0, provider.CreateCount);
        Assert.Equal(0, provider.CancelCount);
        Assert.Empty(await service.ListForCompanyAsync(CompanyId).ContinueWith(task =>
            task.Result.Where(item => item.Status == ManagedResearchJobStatus.Researching)));
    }

    [Fact]
    public async Task Start_rejects_a_stale_investigation_brief_before_it_queues_a_job()
    {
        var queue = new RecordingQueue();
        var service = CreateService(
            new InMemoryManagedResearchJobStore(),
            new InMemoryManagedResearchInvestigationStore(),
            new FakeAgentClient(),
            queue);

        await Assert.ThrowsAsync<ManagedResearchPreviewStaleException>(() => service.StartAsync(
            CompanyId,
            new StartManagedResearchRequest("Research products", ContextRevision: "outdated")));

        Assert.Empty(queue.JobIds);
    }

    [Fact]
    public async Task Provider_failure_becomes_truthful_failed_job_without_leaking_key()
    {
        var provider = new FakeAgentClient { Failure = new ManagedResearchProviderException(
            "Exa Agent rejected the configured API key.", ManagedResearchFailureKind.Authentication) };
        var service = CreateService(
            new InMemoryManagedResearchJobStore(),
            new InMemoryManagedResearchInvestigationStore(),
            provider,
            new RecordingQueue());

        var started = await service.StartAsync(CompanyId, new StartManagedResearchRequest("Research products"));
        var failed = await service.ProcessAsync(started.Id);

        Assert.Equal(ManagedResearchJobStatus.Failed, failed!.Status);
        Assert.Contains("rejected", failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await new InMemoryManagedResearchInvestigationStore().ListForCompanyAsync(CompanyId));
    }

    private static ManagedResearchJobService CreateService(
        IManagedResearchJobStore jobs,
        IManagedResearchInvestigationStore investigations,
        FakeAgentClient provider,
        RecordingQueue queue) =>
        new(
            jobs,
            new InMemoryManagedResearchCompanyContextReader([
                new ManagedResearchCompanyContext
                {
                    CompanyId = CompanyId,
                    DisplayName = "FPT Software",
                    LegalName = "FPT Software Company Limited",
                    OfficialWebsite = "https://fptsoftware.com",
                    Country = "Vietnam",
                    Headquarters = "Hanoi",
                    AcceptedProfileSummary = "A technology services company.",
                    EvidenceGaps = ["Leadership", "EmployeeScale", "Markets"]
                }
            ]),
            provider,
            investigations,
            new TestClock(Now),
            queue);

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class RecordingQueue : IManagedResearchJobQueue
    {
        public List<Guid> JobIds { get; } = [];

        public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JobIds.Add(jobId);
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<Guid> DequeueAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var jobId in JobIds.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return jobId;
                await Task.Yield();
            }
        }
    }

    private sealed class TestClock(DateTimeOffset now) : IManagedResearchClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeAgentClient : IManagedResearchAgentClient
    {
        public ManagedResearchProviderException? Failure { get; init; }
        public ManagedResearchAgentCreateRequest? LastCreateRequest { get; private set; }
        public int CreateCount { get; private set; }
        public int CancelCount { get; private set; }

        public Task<ManagedResearchProviderRun> CreateAsync(ManagedResearchAgentCreateRequest request, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }
            LastCreateRequest = request;
            CreateCount++;
            return Task.FromResult(new ManagedResearchProviderRun(
                "agent_run_1",
                ManagedResearchProviderRunStatus.Queued,
                ExaAgentClient.ProviderId));
        }

        public Task<ManagedResearchProviderRun> GetAsync(string providerRunId, CancellationToken cancellationToken = default)
        {
            using var structured = JsonDocument.Parse("""
                {
                  "summary":"The company serves enterprise customers.",
                  "claims":[{"topic":"Markets","statement":"It serves enterprise customers.","supportingSourceUrls":["https://example.com/about"]}],
                  "sources":[{"title":"About","url":"https://example.com/about","publisher":"Example"}],
                  "uncertainties":[]
                }
                """);
            return Task.FromResult(new ManagedResearchProviderRun(
                providerRunId,
                ManagedResearchProviderRunStatus.Completed,
                ExaAgentClient.ProviderId,
                new ManagedResearchProviderOutput("The company serves enterprise customers.", structured.RootElement.Clone()),
                CompletedAt: Now,
                CostDollars: 0.05m));
        }

        public Task<ManagedResearchProviderRun> CancelAsync(string providerRunId, CancellationToken cancellationToken = default)
        {
            CancelCount++;
            return Task.FromResult(new ManagedResearchProviderRun(providerRunId, ManagedResearchProviderRunStatus.Cancelled, ExaAgentClient.ProviderId));
        }
    }
}
