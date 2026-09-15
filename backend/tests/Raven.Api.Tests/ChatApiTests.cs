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
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Ai;

namespace Raven.Api.Tests;

public sealed class ChatApiTests(RavenApiFactory factory) : IClassFixture<RavenApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task Create_and_get_conversation_pin_the_accepted_profile()
    {
        var client = factory.CreateClient();
        var company = await CreateCompanyAsync(client);
        var profile = await SeedProfileAsync(company.Id, 1, "Example profile");

        var create = await client.PostAsync($"/api/companies/{company.Id}/chat/conversations", null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var conversation = await create.Content.ReadFromJsonAsync<ChatConversationResponse>(JsonOptions);
        Assert.NotNull(conversation);
        Assert.Equal(profile.Id, conversation.ProfileVersionId);
        Assert.Equal(1, conversation.ProfileVersion);

        var get = await client.GetFromJsonAsync<ChatConversationResponse>($"/api/companies/{company.Id}/chat/conversations/{conversation.Id}", JsonOptions);
        Assert.NotNull(get);
        Assert.Equal(conversation.ProfileVersionId, get.ProfileVersionId);
    }

    [Fact]
    public async Task Structured_answer_is_persisted_with_profile_citation()
    {
        var profileAgent = new FakeAgentFactory(new ChatAgentResult(ChatAnswerStatus.Answered, "Verified industry", [], null));
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICompanyChatAgentFactory>();
            services.AddScoped<ICompanyChatAgentFactory>(_ => profileAgent);
        })).CreateClient();
        var company = await CreateCompanyAsync(client);
        var profile = await SeedProfileAsync(company.Id, 1, "Example profile");
        profileAgent.Result = new ChatAgentResult(ChatAnswerStatus.Answered, "Verified industry", [profile.SourceId], null);
        var conversation = await CreateConversationAsync(client, company.Id);

        var response = await client.PostAsJsonAsync($"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages", new CreateChatMessageRequest("What industry is this company in?"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var message = JsonSerializer.Deserialize<SendChatMessageResponse>(body, JsonOptions)!;
        Assert.Equal(ChatAnswerStatus.Answered, message.Status);
        var citation = Assert.Single(message.Citations);
        Assert.Equal(ChatCitationOrigin.Profile, citation.Origin);
        Assert.Equal("primaryIndustry", citation.FieldPath);
        Assert.Equal(profile.SourceId, citation.SourceDocumentId);
    }

    [Fact]
    public async Task External_scope_status_does_not_require_a_keyword_router()
    {
        var profileAgent = new FakeAgentFactory(new ChatAgentResult(ChatAnswerStatus.UnsupportedScope, "This version only answers about the opened company.", [], null));
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICompanyChatAgentFactory>();
            services.AddScoped<ICompanyChatAgentFactory>(_ => profileAgent);
        })).CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);

        var response = await client.PostAsJsonAsync($"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages", new CreateChatMessageRequest("Compare this with another company"));
        var message = await response.Content.ReadFromJsonAsync<SendChatMessageResponse>(JsonOptions);
        Assert.NotNull(message);
        Assert.Equal(ChatAnswerStatus.UnsupportedScope, message.Status);
    }

    [Fact]
    public async Task Structured_insufficient_evidence_status_is_accepted_from_gemini_shape()
    {
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiModelProvider>();
            services.AddScoped<IAiModelProvider>(_ => new FixedAiProvider("""
                {
                  "action": "final",
                  "status": "insufficient_evidence",
                  "answer": "The accepted profile does not contain this information.",
                  "sourceDocumentId": null,
                  "citedSourceDocumentIds": [],
                  "followUpQuestion": null
                }
                """));
        })).CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);

        var response = await client.PostAsJsonAsync($"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages", new CreateChatMessageRequest("What achievements did the company have in 2025?"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var message = JsonSerializer.Deserialize<SendChatMessageResponse>(body, JsonOptions)!;
        Assert.Equal(ChatAnswerStatus.InsufficientEvidence, message.Status);
    }

    [Fact]
    public async Task Invalid_cross_company_citation_is_rejected_and_marked_failed()
    {
        var profileAgent = new FakeAgentFactory(new ChatAgentResult(ChatAnswerStatus.Answered, "unsupported", [Guid.NewGuid()], null));
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICompanyChatAgentFactory>();
            services.AddScoped<ICompanyChatAgentFactory>(_ => profileAgent);
        })).CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);

        var response = await client.PostAsJsonAsync($"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages", new CreateChatMessageRequest("What is the industry?"));
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ai_invalid_response", problem.GetProperty("code").GetString());

        var history = await client.GetFromJsonAsync<ChatConversationResponse>($"/api/companies/{company.Id}/chat/conversations/{conversation.Id}", JsonOptions);
        var assistant = Assert.Single(history!.Messages, item => item.Role == ChatMessageRole.Assistant);
        Assert.Equal(ChatMessageStatus.Failed, assistant.Status);
    }

    [Fact]
    public async Task Conversation_routes_enforce_company_ownership()
    {
        var client = factory.CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);
        var otherCompany = await CreateCompanyAsync(client);

        var response = await client.GetAsync($"/api/companies/{otherCompany.Id}/chat/conversations/{conversation.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<CompanyResponse> CreateCompanyAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/companies", new CreateCompanyRequest($"Chat Company {Guid.NewGuid():N}", "https://example.com", "Vietnam"));
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

    private async Task<SeededProfile> SeedProfileAsync(Guid companyId, int version, string displayName)
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
            Content = "industry evidence",
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
        public ICompanyChatAgent Create() => new FakeAgent(this);

        private sealed class FakeAgent(FakeAgentFactory owner) : ICompanyChatAgent
        {
            public Task<ChatAgentCompletion> RunAsync(ChatAgentRequest request, CancellationToken cancellationToken = default) =>
                Task.FromResult(new ChatAgentCompletion(owner.Result, "fake", "fake-model", []));
        }
    }

    private sealed class FixedAiProvider(string response) : IAiModelProvider
    {
        public string Id => "fake";

        public Task<AiModelResult> GenerateStructuredAsync(AiModelRequest request, CancellationToken cancellationToken = default)
        {
            using var document = JsonDocument.Parse(response);
            return Task.FromResult(new AiModelResult(
                "fake",
                request.Model,
                document.RootElement.Clone(),
                TimeSpan.Zero));
        }
    }
}
