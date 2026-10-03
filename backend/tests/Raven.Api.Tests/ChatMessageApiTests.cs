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

public sealed partial class ChatApiTests
{
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
    public async Task Web_answer_persists_only_message_scoped_evidence_and_does_not_mutate_profile_sources()
    {
        var completion = new ChatAgentCompletion(
            new ChatAgentResult(ChatAnswerStatus.Answered, "The company published a current update.", [], null, ["r1"]),
            "fake",
            "fake-model",
            [],
            [new ChatWebEvidenceDraft("r1", "https://example.com/news", "https://example.com/news", "Current update", "Public update", "Verified current evidence", "fake-search", "fake-crawler", 1, DateTimeOffset.UtcNow)]);
        var agent = new FakeCompletionAgentFactory(completion);
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICompanyChatAgentFactory>();
            services.AddScoped<ICompanyChatAgentFactory>(_ => agent);
        })).CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);
        int sourcesBefore;
        using (var scope = factory.Services.CreateScope())
        {
            sourcesBefore = await scope.ServiceProvider.GetRequiredService<RavenDbContext>().SourceDocuments.CountAsync(source => source.CompanyId == company.Id);
        }

        var response = await client.PostAsJsonAsync($"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages", new CreateChatMessageRequest("What changed recently?"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var message = JsonSerializer.Deserialize<SendChatMessageResponse>(body, JsonOptions)!;
        var citation = Assert.Single(message.Citations);
        Assert.Equal(ChatCitationOrigin.Web, citation.Origin);
        Assert.Null(citation.SourceDocumentId);
        Assert.Single(message.WebEvidenceSnapshots);

        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<RavenDbContext>();
        Assert.Equal(sourcesBefore, await db.SourceDocuments.CountAsync(source => source.CompanyId == company.Id));
        Assert.Equal(1, await db.ChatWebEvidenceSnapshots.CountAsync(snapshot => snapshot.ChatMessageId == message.MessageId));
    }
    [Fact]
    public async Task Streamed_message_emits_progress_and_final_response()
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

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages/stream")
        {
            Content = JsonContent.Create(new CreateChatMessageRequest("What industry is this company in?"))
        };
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, body);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("event: progress", body, StringComparison.Ordinal);
        Assert.Contains("Analyzing", body, StringComparison.Ordinal);
        Assert.Contains("CheckingProfile", body, StringComparison.Ordinal);
        Assert.DoesNotContain("WebSearching", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Crawling", body, StringComparison.Ordinal);
        Assert.Contains("event: completed", body, StringComparison.Ordinal);
        Assert.Contains("Verified industry", body, StringComparison.Ordinal);
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
    public async Task Greeting_is_persisted_without_company_citation_or_ai_call()
    {
        var profileAgent = new FakeAgentFactory(new ChatAgentResult(ChatAnswerStatus.Answered, "This must not run", [], null));
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICompanyChatAgentFactory>();
            services.AddScoped<ICompanyChatAgentFactory>(_ => profileAgent);
        })).CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);

        var response = await client.PostAsJsonAsync($"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages", new CreateChatMessageRequest("hi"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var message = JsonSerializer.Deserialize<SendChatMessageResponse>(body, JsonOptions)!;
        Assert.Equal(ChatAnswerStatus.Conversational, message.Status);
        Assert.Empty(message.Citations);
        Assert.Contains("Ask", message.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Structured_insufficient_evidence_status_is_accepted_from_gemini_shape()
    {
        var provider = new SequencedAiProvider([
            """
            {"questionKind":"company_factual","facets":["achievements"],"requestedYears":["2025"],"query":"achievements 2025","profileSourceDocumentIds":[],"answer":null,"followUpQuestion":null}
            """,
            """
            {"status":"insufficient_evidence","answer":"The accepted profile does not contain this information.","citedSourceDocumentIds":[],"citedWebEvidenceCandidateIds":[],"citedInvestigationIds":[],"claims":[],"limitations":["No verified evidence"],"followUpQuestion":null}
            """
        ]);
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiModelProvider>();
            services.AddScoped<IAiModelProvider>(_ => provider);
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
    public async Task Short_person_questions_use_the_pinned_profile_and_relevant_late_source_excerpt()
    {
        var ai = new SequencedAiProvider([
            """
            {"questionKind":"company_factual","facets":["leadership"],"requestedYears":[],"query":null,"profileSourceDocumentIds":[],"answer":null,"followUpQuestion":null}
            """,
            """
            {"status":"insufficient_evidence","answer":"The pinned profile has no such leader.","citedSourceDocumentIds":[],"citedWebEvidenceCandidateIds":[],"citedInvestigationIds":[],"citedBriefingVersionIds":[],"claims":[],"limitations":[],"followUpQuestion":null}
            """
        ]);
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiModelProvider>();
            services.AddScoped<IAiModelProvider>(_ => ai);
        })).CreateClient();
        var company = await CreateCompanyAsync(client, $"Apple Inc. {Guid.NewGuid():N}");
        var sourceContent = new string('x', 28_000) +
            "\n\n# Founders\nSteve Jobs co-founded the company.\n\n" +
            new string('y', 40_000) + "\n\n# Leadership\nSteve Jobs served as interim CEO.";
        await SeedProfileAsync(company.Id, 1, company.Name);
        var olderConversation = await CreateConversationAsync(client, company.Id);
        await SeedProfileAsync(company.Id, 2, company.Name, "Steve Jobs", "interim CEO", sourceContent);
        var conversation = await CreateConversationAsync(client, company.Id);
        Assert.Equal(2, conversation.ProfileVersion);
        Assert.False(conversation.WebSearchEnabled);

        foreach (var question in new[] { "Steve Jobs", "Steve Jobs là ai?", "trong profile có thông tin của Steve Jobs không?" })
        {
            var response = await client.PostAsJsonAsync(
                $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages",
                new CreateChatMessageRequest(question));
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            var message = JsonSerializer.Deserialize<SendChatMessageResponse>(body, JsonOptions)!;
            Assert.Equal(ChatAnswerStatus.Answered, message.Status);
            Assert.Contains("Steve Jobs", message.Answer, StringComparison.Ordinal);
            Assert.Contains("interim CEO", message.Answer, StringComparison.Ordinal);
            Assert.Single(message.Citations, citation => citation.Origin == ChatCitationOrigin.Profile);
            Assert.Contains(message.ToolExecutions, execution => execution.Tool == "get_source_excerpt" && execution.Status == "succeeded");
        }
        Assert.Empty(ai.Requests);

        var olderResponse = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{olderConversation.Id}/messages",
            new CreateChatMessageRequest("Steve Jobs"));
        olderResponse.EnsureSuccessStatusCode();
        var olderMessage = (await olderResponse.Content.ReadFromJsonAsync<SendChatMessageResponse>(JsonOptions))!;
        Assert.Equal(ChatAnswerStatus.InsufficientEvidence, olderMessage.Status);
        Assert.Equal(2, ai.Requests.Count);
    }

    [Fact]
    public async Task Chat_uses_the_configured_chat_model_not_the_profile_model()
    {
        var provider = new SequencedAiProvider([
            """
            {"questionKind":"company_factual","facets":["achievements"],"requestedYears":[],"query":"achievements","profileSourceDocumentIds":[],"answer":null,"followUpQuestion":null}
            """,
            """
            {"status":"insufficient_evidence","answer":"No verified answer.","citedSourceDocumentIds":[],"citedWebEvidenceCandidateIds":[],"citedInvestigationIds":[],"claims":[],"limitations":["No verified evidence"],"followUpQuestion":null}
            """
        ]);
        var settings = new ResearchSettingsService(new InMemoryResearchSettingsStore());
        var defaults = await settings.GetAsync();
        await settings.UpdateAsync(new UpdateResearchSettingsRequest(
            defaults.GroundingMode, defaults.ProfileModel, defaults.GroundingModel,
            defaults.DeepResearchModel, defaults.AiSourceRerankingEnabled, defaults.ProviderPreset,
            defaults.SearchProviderPriority, defaults.CrawlerProviderPriority,
            ChatModel: "gemini-3.8-flash"));
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiModelProvider>();
            services.AddScoped<IAiModelProvider>(_ => provider);
            services.RemoveAll<IResearchSettingsService>();
            services.AddSingleton<IResearchSettingsService>(settings);
        })).CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages",
            new CreateChatMessageRequest("What achievements did the company have?"));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.All(provider.Requests, request => Assert.Equal("gemini-3.8-flash", request.Model));
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

        var patch = await client.PatchAsJsonAsync(
            $"/api/companies/{otherCompany.Id}/chat/conversations/{conversation.Id}/capabilities",
            new UpdateChatCapabilitiesRequest(true));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }
}
