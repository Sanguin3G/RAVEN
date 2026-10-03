using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raven.Api.Data;
using Raven.Api.Features.Chat;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Settings;
using Raven.Api.Features.ManagedResearch;
using Raven.Api.Features.Search;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research.Briefings;
using Raven.Api.Features.Research.SavedArtifacts;

namespace Raven.Api.Tests;

public sealed partial class ChatApiTests(RavenApiFactory factory) : IClassFixture<RavenApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task Provider_health_uses_recent_gemini_telemetry_and_omits_raw_error_messages()
    {
        var model = $"test-model-{Guid.NewGuid():N}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
            db.ResearchEvents.Add(new ResearchEvent
            {
                Category = ResearchEventCategory.AI,
                Operation = "company_chat",
                Status = ResearchEventStatus.Failed,
                Provider = "gemini",
                Model = model,
                Sequence = DateTimeOffset.UtcNow.UtcTicks,
                Timestamp = DateTimeOffset.UtcNow.AddSeconds(-20),
                HttpStatus = 429,
                ErrorCode = "resource_exhausted",
                ErrorMessage = "raw provider details must not reach the status page"
            });
            await db.SaveChangesAsync();
        }

        using var httpResponse = await factory.CreateClient().GetAsync("/api/system/provider-health");
        var body = await httpResponse.Content.ReadAsStringAsync();
        Assert.True(httpResponse.IsSuccessStatusCode, body);
        var response = JsonSerializer.Deserialize<ProviderHealthResponse>(body, JsonOptions);
        Assert.NotNull(response);
        var health = Assert.Single(response.Models, item => item.Provider == "gemini" && item.Model == model);
        Assert.Equal("Degraded", health.State);
        Assert.Equal(429, health.LastFailureHttpStatus);
        Assert.Equal("Rate limited by the provider.", health.LastFailureSummary);
        Assert.Equal(1, health.RequestsLastMinute);
        var activity = Assert.Single(response.RecentActivity, item => item.Provider == "gemini" && item.Model == model);
        Assert.Equal("RateLimited", activity.FailureKind);
        Assert.DoesNotContain("raw provider details must not reach the status page", JsonSerializer.Serialize(response));
    }








    private static async Task<CompanyResponse> CreateCompanyAsync(HttpClient client, string? name = null)
    {
        var response = await client.PostAsJsonAsync("/api/companies", new CreateCompanyRequest(name ?? $"Chat Company {Guid.NewGuid():N}", "https://example.com", "Vietnam"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompanyResponse>())!;
    }

    private static async Task<ChatConversationResponse> CreateConversationAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsync($"/api/companies/{companyId}/chat/conversations", null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonSerializer.Deserialize<ChatConversationResponse>(body, JsonOptions)!;
    }

    private async Task<SeededProfile> SeedProfileAsync(Guid companyId, int version, string displayName,
        string? leaderName = null, string? leaderTitle = null, string? sourceContent = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
        var run = new ResearchRun
        {
            CompanyId = companyId,
            RequestedSearchProvider = "fake",
            RequestedCrawlerProvider = "fake",
            Status = ResearchRunStatus.Completed,
            Stage = ResearchStage.Completed
        };
        var source = new SourceDocument
        {
            CompanyId = companyId,
            ResearchRunId = run.Id,
            Url = "https://example.com/profile",
            NormalizedUrl = "https://example.com/profile",
            SourceKind = SourceKind.OfficialWebsite,
            Title = "Profile",
            SourceDomain = "example.com",
            RetrievedAt = DateTimeOffset.UtcNow,
            Content = sourceContent ?? "industry evidence",
            ContentHash = Guid.NewGuid().ToString("N").PadRight(64, '0'),
            CrawlerProvider = "fake"
        };
        var profile = new CompanyProfileVersion
        {
            CompanyId = companyId,
            ResearchRunId = run.Id,
            Version = version,
            GeneratedAt = DateTimeOffset.UtcNow,
            ConfirmedAt = DateTimeOffset.UtcNow,
            DisplayName = displayName,
            PrimaryIndustry = "Software",
            ProfileJson = "{}"
        };
        var evidence = new ProfileEvidence { CompanyProfileVersionId = profile.Id, FieldPath = "primaryIndustry", SourceDocumentIdsJson = JsonSerializer.Serialize(new[] { source.Id }) };
        evidence.SourceDocumentIds.Add(source.Id);
        profile.Evidence.Add(evidence);
        if (leaderName is not null)
        {
            profile.Leadership.Add(new ProfileLeader(leaderName, leaderTitle));
            var leadershipEvidence = new ProfileEvidence
            {
                CompanyProfileVersionId = profile.Id,
                FieldPath = "leadership",
                SourceDocumentIdsJson = JsonSerializer.Serialize(new[] { source.Id })
            };
            leadershipEvidence.SourceDocumentIds.Add(source.Id);
            profile.Evidence.Add(leadershipEvidence);
            db.ProfileEvidences.Add(leadershipEvidence);
        }
        profile.ProfileJson = JsonSerializer.Serialize(profile, JsonOptions);
        db.ResearchRuns.Add(run);
        db.SourceDocuments.Add(source);
        db.CompanyProfileVersions.Add(profile);
        db.ProfileEvidences.Add(evidence);
        await db.SaveChangesAsync();
        return new SeededProfile(profile.Id, source.Id);
    }

    private sealed record SeededProfile(Guid Id, Guid SourceId);

    private sealed class FakeAgentFactory(ChatAgentResult result) : ICompanyChatAgentFactory
    {
        public ChatAgentResult Result { get; set; } = result;
        public ChatAgentRequest? LastRequest { get; private set; }
        public ICompanyChatAgent Create() => new FakeAgent(this);

        private sealed class FakeAgent(FakeAgentFactory owner) : ICompanyChatAgent
        {
            public Task<ChatAgentCompletion> RunAsync(ChatAgentRequest request, CancellationToken cancellationToken = default)
            {
                owner.LastRequest = request;
                return Task.FromResult(new ChatAgentCompletion(owner.Result, "fake", "fake-model", []));
            }
        }
    }

    private sealed class FakeCompletionAgentFactory(ChatAgentCompletion completion) : ICompanyChatAgentFactory
    {
        public ICompanyChatAgent Create() => new FakeAgent(completion);

        private sealed class FakeAgent(ChatAgentCompletion completion) : ICompanyChatAgent
        {
            public Task<ChatAgentCompletion> RunAsync(ChatAgentRequest request, CancellationToken cancellationToken = default) =>
                Task.FromResult(completion);
        }
    }
    private sealed class SequencedAiProvider(IEnumerable<string> responses) : IAiModelProvider
    {
        private readonly Queue<string> responses = new(responses);
        public string Id => "fake-sequenced";
        public List<AiModelRequest> Requests { get; } = [];

        public Task<AiModelResult> GenerateStructuredAsync(AiModelRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (responses.Count == 0) throw new InvalidOperationException("No configured AI response remains.");
            var response = responses.Dequeue();
            if (response.Contains("__WEB_ID__", StringComparison.Ordinal))
            {
                var webId = request.ResponseSchema.GetProperty("properties")
                    .GetProperty("citedWebEvidenceCandidateIds").GetProperty("items")
                    .GetProperty("enum")[0].GetString()!;
                response = response.Replace("__WEB_ID__", webId, StringComparison.Ordinal);
            }
            using var document = JsonDocument.Parse(response);
            return Task.FromResult(new AiModelResult("fake", request.Model, document.RootElement.Clone(), TimeSpan.Zero));
        }
    }

    private sealed class RecordingSearchProvider : ISearchProvider
    {
        public string Id => "fake-search";
        public List<SearchRequest> Requests { get; } = [];

        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new SearchResponse(Id, [
                new SearchResult("FPT Software achievements 2023–2025", "https://example.com/fpt-achievements", "Awards and milestones in 2023, 2024 and 2025", 1),
                new SearchResult("UNCRAWLED_CANDIDATE_MARKER", "https://other.example.org/unread", "FPT Software secondary candidate", 2)
            ]));
        }
    }

    private sealed class RecordingCrawlerProvider : ICrawlerProvider
    {
        public string Id => "fake-crawler";
        public int CallCount { get; private set; }

        public Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new CrawlResult(Id, request.Url, request.Url, "FPT Software achievements",
                "# Achievements\n\nFPT Software received verified recognition in 2023. Further awards followed in 2024 and 2025.",
                true, null, DateTimeOffset.Parse("2026-09-24T00:00:00Z")));
        }
    }

    private sealed class MultiRoundSearchProvider : ISearchProvider
    {
        public string Id => "fake-search";
        public List<SearchRequest> Requests { get; } = [];

        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var year = Requests.Count == 1 ? "2023" : "2024";
            return Task.FromResult(new SearchResponse(Id, [
                new SearchResult($"Award {year}", $"https://awards.example.org/{year}", $"Company award {year}", 1)
            ]));
        }
    }

    private sealed class MultiRoundCrawlerProvider : ICrawlerProvider
    {
        public string Id => "fake-crawler";
        public int CallCount { get; private set; }

        public Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var year = request.Url.EndsWith("2023", StringComparison.Ordinal) ? "2023" : "2024";
            return Task.FromResult(new CrawlResult(Id, request.Url, request.Url, $"Award {year}",
                $"# Award {year}\n\nEvidence for {year} was verified by the award organizer.", true, null,
                DateTimeOffset.Parse("2026-09-24T00:00:00Z")));
        }
    }
}
