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

public sealed class ChatApiTests(RavenApiFactory factory) : IClassFixture<RavenApiFactory>
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
        Assert.False(conversation.WebSearchEnabled);

        var get = await client.GetFromJsonAsync<ChatConversationResponse>($"/api/companies/{company.Id}/chat/conversations/{conversation.Id}", JsonOptions);
        Assert.NotNull(get);
        Assert.Equal(conversation.ProfileVersionId, get.ProfileVersionId);
        Assert.False(get.WebSearchEnabled);
    }

    [Fact]
    public async Task Web_search_capability_is_persisted_per_conversation()
    {
        var client = factory.CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);

        var patch = await client.PatchAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/capabilities",
            new UpdateChatCapabilitiesRequest(true));
        var body = await patch.Content.ReadAsStringAsync();
        Assert.True(patch.IsSuccessStatusCode, body);
        var updated = JsonSerializer.Deserialize<ChatConversationResponse>(body, JsonOptions)!;
        Assert.True(updated.WebSearchEnabled);

        var restored = await client.GetFromJsonAsync<ChatConversationResponse>(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}", JsonOptions);
        Assert.NotNull(restored);
        Assert.True(restored.WebSearchEnabled);
    }

    [Fact]
    public async Task Conversation_history_returns_web_evidence_snapshots_separately_from_profile_sources()
    {
        var client = factory.CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);

        var assistant = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatMessageRole.Assistant,
            Content = "The company published this update.",
            Status = ChatMessageStatus.Completed,
            AnswerStatus = ChatAnswerStatus.Answered
        };
        var snapshot = new ChatWebEvidenceSnapshot
        {
            ChatMessageId = assistant.Id,
            Url = "https://example.com/news/update",
            NormalizedUrl = "https://example.com/news/update",
            Title = "Company update",
            SearchSnippet = "A public company update.",
            ContentExcerpt = "The company published an update in September.",
            SearchProvider = "fake-search",
            CrawlerProvider = "fake-crawler",
            SearchRank = 1,
            RetrievedAt = DateTimeOffset.Parse("2026-09-17T00:00:00Z")
        };
        var citation = new ChatCitation
        {
            ChatMessageId = assistant.Id,
            WebEvidenceSnapshotId = snapshot.Id,
            Origin = ChatCitationOrigin.Web
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
            db.AddRange(assistant, snapshot, citation);
            await db.SaveChangesAsync();
        }

        var restored = await client.GetFromJsonAsync<ChatConversationResponse>(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}", JsonOptions);
        var message = Assert.Single(restored!.Messages);
        var evidence = Assert.Single(message.WebEvidenceSnapshots);
        Assert.Equal(snapshot.Id, evidence.Id);
        Assert.Equal("fake-search", evidence.SearchProvider);
        var returnedCitation = Assert.Single(message.Citations);
        Assert.Equal(ChatCitationOrigin.Web, returnedCitation.Origin);
        Assert.Null(returnedCitation.SourceDocumentId);
        Assert.Equal(snapshot.Id, returnedCitation.WebEvidenceSnapshotId);
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

    [Fact]
    public async Task Attached_investigation_grounds_chat_and_citation_is_restored()
    {
        var agent = new FakeAgentFactory(new ChatAgentResult(ChatAnswerStatus.Conversational, "Ready", [], null));
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICompanyChatAgentFactory>();
            services.AddScoped<ICompanyChatAgentFactory>(_ => agent);
        }));
        var client = app.CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);
        var investigation = new ManagedResearchInvestigation
        {
            CompanyId = company.Id, JobId = Guid.NewGuid(), Origin = "ManagedAi", Objective = "Which markets are served?",
            Summary = "Research found an enterprise market.",
            ResultJson = JsonSerializer.Serialize(new ManagedResearchResult("fake", "Which markets are served?",
                "Research found an enterprise market.",
                [new ManagedResearchClaim("Markets", "Enterprise customers are served.", ["https://example.com/markets"])],
                [new ManagedResearchSource("Markets", "https://example.com/markets")])),
            CompletedAt = DateTimeOffset.UtcNow
        };
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
            db.ManagedResearchInvestigations.Add(investigation);
            await db.SaveChangesAsync();
        }

        var attach = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/managed-research/{investigation.Id}/context-attachments",
            new AttachResearchContextRequest(conversation.Id));
        Assert.Equal(HttpStatusCode.Created, attach.StatusCode);
        agent.Result = new ChatAgentResult(ChatAnswerStatus.Answered,
            "The Investigation reports enterprise customers.", [], null, [], [investigation.Id]);
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages",
            new CreateChatMessageRequest("What did the Investigation find?"));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Contains(agent.LastRequest!.Investigations!, item => item.Id == investigation.Id &&
            item.Material.Contains("Enterprise customers are served.", StringComparison.Ordinal));
        var restored = await client.GetFromJsonAsync<ChatConversationResponse>(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}", JsonOptions);
        var assistant = Assert.Single(restored!.Messages, item => item.Role == ChatMessageRole.Assistant);
        var citation = Assert.Single(assistant.Citations);
        Assert.Equal(ChatCitationOrigin.Investigation, citation.Origin);
        Assert.Equal(investigation.Id, citation.InvestigationId);

        var other = await CreateCompanyAsync(client);
        await SeedProfileAsync(other.Id, 1, "Other profile");
        var otherConversation = await CreateConversationAsync(client, other.Id);
        var wrongCompany = await client.PostAsJsonAsync(
            $"/api/companies/{other.Id}/managed-research/{investigation.Id}/context-attachments",
            new AttachResearchContextRequest(otherConversation.Id));
        Assert.Equal(HttpStatusCode.NotFound, wrongCompany.StatusCode);
        var wrongConversation = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/managed-research/{investigation.Id}/context-attachments",
            new AttachResearchContextRequest(otherConversation.Id));
        Assert.Equal(HttpStatusCode.NotFound, wrongConversation.StatusCode);
        var wrongList = await client.GetAsync(
            $"/api/companies/{company.Id}/research-context-attachments?conversationId={otherConversation.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrongList.StatusCode);
        var wrongRemoval = await client.DeleteAsync(
            $"/api/companies/{company.Id}/managed-research/{investigation.Id}/context-attachments?conversationId={otherConversation.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrongRemoval.StatusCode);
    }

    [Fact]
    public async Task Completed_chat_research_attaches_and_answers_once()
    {
        var agent = new FakeAgentFactory(new ChatAgentResult(ChatAnswerStatus.Conversational, "Ready", [], null));
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICompanyChatAgentFactory>();
            services.AddScoped<ICompanyChatAgentFactory>(_ => agent);
        }));
        var client = app.CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);
        var user = new ChatMessage { ConversationId = conversation.Id, Role = ChatMessageRole.User,
            Content = "What markets does this company serve?", Status = ChatMessageStatus.Completed };
        var job = new ManagedResearchJob { CompanyId = company.Id, ConversationId = conversation.Id,
            ChatMessageId = user.Id, AnswerInChat = true, Objective = user.Content,
            ProviderQuery = user.Content };
        var investigation = new ManagedResearchInvestigation { CompanyId = company.Id,
            JobId = job.Id, ConversationId = conversation.Id, ChatMessageId = user.Id, Origin = "ManagedAi",
            Objective = user.Content, Summary = "Enterprise customers are served.",
            ResultJson = JsonSerializer.Serialize(new ManagedResearchResult("fake", user.Content,
                "Enterprise customers are served.")), CompletedAt = DateTimeOffset.UtcNow };
        job.Complete(investigation.ResultJson, investigation.Id, investigation.CompletedAt);
        agent.Result = new ChatAgentResult(ChatAnswerStatus.Answered,
            "The Investigation reports enterprise customers.", [], null, [], [investigation.Id]);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
            db.AddRange(user, job, investigation);
            await db.SaveChangesAsync();
        }
        using (var scope = app.Services.CreateScope())
        {
            var bridge = scope.ServiceProvider.GetRequiredService<ManagedResearchChatBridge>();
            await bridge.CompleteAsync(job, CancellationToken.None);
            await bridge.CompleteAsync(job, CancellationToken.None);
        }
        var restored = await client.GetFromJsonAsync<ChatConversationResponse>(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}", JsonOptions);
        Assert.Equal(2, restored!.Messages.Count);
        var assistant = Assert.Single(restored.Messages, item => item.Role == ChatMessageRole.Assistant);
        Assert.Equal(ChatMessageStatus.Completed, assistant.Status);
        Assert.Equal(investigation.Id, Assert.Single(assistant.Citations).InvestigationId);
        Assert.Contains(agent.LastRequest!.Investigations!, item => item.Id == investigation.Id);
    }

    [Fact]
    public async Task Chat_deep_research_requires_profile_and_persists_approved_question_at_start()
    {
        var client = factory.CreateClient();
        var company = await CreateCompanyAsync(client);
        var rejected = await client.PostAsJsonAsync($"/api/companies/{company.Id}/managed-research",
            new StartManagedResearchRequest("What does this company sell?", Guid.NewGuid(),
                AnswerInChat: true));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);
        var started = await client.PostAsJsonAsync($"/api/companies/{company.Id}/managed-research",
            new StartManagedResearchRequest("What does this company sell?", conversation.Id,
                AnswerInChat: true));
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var job = await started.Content.ReadFromJsonAsync<ManagedResearchJobResponse>(JsonOptions);
        Assert.NotNull(job);
        Assert.True(job.AnswerInChat);
        Assert.NotNull(job.ChatMessageId);
        var history = await client.GetFromJsonAsync<ChatConversationResponse>(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}", JsonOptions);
        var user = Assert.Single(history!.Messages, item => item.Role == ChatMessageRole.User);
        Assert.Equal(job.ChatMessageId, user.Id);
        Assert.Equal(job.Objective, user.Content);
    }

    [Fact]
    public async Task Web_enabled_factual_question_searches_crawls_accumulates_evidence_and_returns_citations()
    {
        var ai = new SequencedAiProvider([
            """
            {"questionKind":"company_factual","facets":["achievements","awards"],"requestedYears":["2023","2024","2025"],"query":"achievements awards 2023 2024 2025","profileSourceDocumentIds":[],"answer":null,"followUpQuestion":null}
            """,
            """
            {"sufficient":true,"missingEvidence":[],"nextQuery":null,"candidateIds":[]}
            """,
            """
            {"status":"answered","answer":"Nguồn chưa crawl cũng có thành tích.","citedSourceDocumentIds":[],"citedWebEvidenceCandidateIds":["r2"],"citedInvestigationIds":[],"claims":[{"text":"Nguồn chưa crawl.","evidenceIds":["web:r2"]}],"limitations":[],"followUpQuestion":null}
            """,
            """
            {"status":"answered","answer":"FPT Software có các thành tích đã được xác minh trong giai đoạn 2023–2025.","citedSourceDocumentIds":[],"citedWebEvidenceCandidateIds":["r1"],"citedInvestigationIds":[],"claims":[{"text":"Các thành tích đã được xác minh.","evidenceIds":["web:r1"]}],"limitations":[],"followUpQuestion":null}
            """
        ]);
        var search = new RecordingSearchProvider();
        var crawler = new RecordingCrawlerProvider();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiModelProvider>();
            services.AddScoped<IAiModelProvider>(_ => ai);
            services.RemoveAll<ISearchProvider>();
            services.AddScoped<ISearchProvider>(_ => search);
            services.RemoveAll<ICrawlerProvider>();
            services.AddScoped<ICrawlerProvider>(_ => crawler);
            services.PostConfigure<ChatResearchOptions>(options => options.MaxParallelCrawls = 1);
        }));
        var client = app.CreateClient();
        var company = await CreateCompanyAsync(client, "FPT Software");
        await SeedProfileAsync(company.Id, 1, "FPT Software");
        var conversation = await CreateConversationAsync(client, company.Id);
        var capability = await client.PatchAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/capabilities",
            new UpdateChatCapabilitiesRequest(true));
        capability.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages",
            new CreateChatMessageRequest("Các thành tích từ năm 2023 đến năm 2025 của FPT Software là gì?"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, body);
        var message = JsonSerializer.Deserialize<SendChatMessageResponse>(body, JsonOptions)!;
        Assert.Equal(ChatAnswerStatus.Answered, message.Status);
        Assert.Contains(message.ToolExecutions, item => item.Tool == "search_web" && item.Status == "succeeded");
        Assert.Contains(message.ToolExecutions, item => item.Tool == "read_web_page" && item.Status == "succeeded");
        Assert.Single(message.WebEvidenceSnapshots);
        Assert.Single(message.Citations, item => item.Origin == ChatCitationOrigin.Web);
        Assert.Contains("FPT Software", search.Requests[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2023", search.Requests[0].Query, StringComparison.Ordinal);
        Assert.Contains("2025", search.Requests[0].Query, StringComparison.Ordinal);
        Assert.Equal(1, crawler.CallCount);
        Assert.Equal(4, ai.Requests.Count);
        Assert.DoesNotContain("UNCRAWLED_CANDIDATE_MARKER", ai.Requests[2].Prompt, StringComparison.Ordinal);
        Assert.Equal("company-chat-research-final-v1-repair", ai.Requests[3].PromptTemplateVersion);
        var allowedWebIds = ai.Requests[2].ResponseSchema.GetProperty("properties")
            .GetProperty("citedWebEvidenceCandidateIds").GetProperty("items").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(["r1"], allowedWebIds);
    }

    [Fact]
    public async Task Attached_briefing_pins_an_exact_version_and_restores_its_citation()
    {
        var agent = new FakeAgentFactory(new ChatAgentResult(ChatAnswerStatus.Conversational, "Ready", [], null));
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICompanyChatAgentFactory>();
            services.AddScoped<ICompanyChatAgentFactory>(_ => agent);
        }));
        var client = app.CreateClient();
        var company = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Example profile");
        var conversation = await CreateConversationAsync(client, company.Id);
        var sourceInvestigationId = Guid.NewGuid();
        var briefing = new ResearchBriefing
        {
            CompanyId = company.Id,
            Title = "Markets Briefing",
            Template = "Markets & Expansion",
            Objective = "Summarize market expansion"
        };
        var source = new BriefingSourceSnapshot(
            sourceInvestigationId, InvestigationMaterialKind.Managed, null, "Japan expansion research",
            "ManagedAi", InvestigationPurpose.GeneralResearch, ["Markets"], DateTimeOffset.UtcNow.AddDays(-2),
            "A source summary that must not be copied as raw context.", [], [], ["Customer count remains uncertain."],
            "RAW_SECRET_MATERIAL_MUST_NOT_ENTER_CHAT");
        var versionOne = new ResearchBriefingVersion
        {
            BriefingId = briefing.Id,
            VersionNumber = 1,
            GeneratedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ResearchThrough = DateTimeOffset.UtcNow.AddDays(-2),
            Title = briefing.Title,
            Template = briefing.Template,
            Objective = briefing.Objective,
            SectionsJson = JsonSerializer.Serialize(new[]
            {
                new BriefingSection("markets", "Markets", ["Japan expansion is underway."], [sourceInvestigationId])
            }, JsonOptions),
            SourcesJson = JsonSerializer.Serialize(new[] { source }, JsonOptions)
        };
        var versionTwo = new ResearchBriefingVersion
        {
            BriefingId = briefing.Id,
            VersionNumber = 2,
            GeneratedAt = DateTimeOffset.UtcNow,
            ResearchThrough = DateTimeOffset.UtcNow,
            Title = briefing.Title,
            Template = briefing.Template,
            Objective = briefing.Objective,
            SectionsJson = JsonSerializer.Serialize(new[]
            {
                new BriefingSection("markets", "Markets", ["A newer version exists."], [sourceInvestigationId])
            }, JsonOptions),
            SourcesJson = JsonSerializer.Serialize(new[] { source }, JsonOptions)
        };
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
            db.AddRange(briefing, versionOne, versionTwo);
            await db.SaveChangesAsync();
        }

        var attach = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/briefings/{briefing.Id}/versions/1/context-attachments",
            new AttachResearchContextRequest(conversation.Id));
        Assert.Equal(HttpStatusCode.Created, attach.StatusCode);
        var attached = await attach.Content.ReadFromJsonAsync<ResearchContextAttachmentResponse>(JsonOptions);
        Assert.NotNull(attached);
        Assert.Equal("Briefing", attached.Kind);
        Assert.Equal(versionOne.Id, attached.BriefingVersionId);

        agent.Result = new ChatAgentResult(ChatAnswerStatus.Answered,
            "The pinned Briefing reports Japan expansion.", [], null, [], [], [versionOne.Id]);
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages",
            new CreateChatMessageRequest("What does the Markets Briefing say?"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var requestBriefing = Assert.Single(agent.LastRequest!.Briefings!);
        Assert.Equal(versionOne.Id, requestBriefing.VersionId);
        Assert.Contains("Japan expansion is underway.", requestBriefing.Material, StringComparison.Ordinal);
        Assert.Contains("Customer count remains uncertain.", requestBriefing.Material, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_SECRET_MATERIAL_MUST_NOT_ENTER_CHAT", requestBriefing.Material, StringComparison.Ordinal);
        Assert.DoesNotContain("A source summary that must not be copied as raw context.", requestBriefing.Material, StringComparison.Ordinal);

        var sent = JsonSerializer.Deserialize<SendChatMessageResponse>(body, JsonOptions)!;
        var sentCitation = Assert.Single(sent.Citations);
        Assert.Equal(ChatCitationOrigin.Briefing, sentCitation.Origin);
        Assert.Equal(versionOne.Id, sentCitation.BriefingVersionId);
        Assert.Contains($"briefingVersion=1&conversation={conversation.Id:D}", sentCitation.Url, StringComparison.Ordinal);

        var restored = await client.GetFromJsonAsync<ChatConversationResponse>(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}", JsonOptions);
        var assistant = Assert.Single(restored!.Messages, item => item.Role == ChatMessageRole.Assistant);
        var restoredCitation = Assert.Single(assistant.Citations);
        Assert.Equal(ChatCitationOrigin.Briefing, restoredCitation.Origin);
        Assert.Equal(versionOne.Id, restoredCitation.BriefingVersionId);
        Assert.Contains($"briefingVersion=1&conversation={conversation.Id:D}", restoredCitation.Url, StringComparison.Ordinal);

        var updatePin = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/briefings/{briefing.Id}/versions/2/context-attachments",
            new AttachResearchContextRequest(conversation.Id));
        Assert.Equal(HttpStatusCode.Created, updatePin.StatusCode);
        var attachments = await client.GetFromJsonAsync<ResearchContextAttachmentResponse[]>(
            $"/api/companies/{company.Id}/research-context-attachments?conversationId={conversation.Id}", JsonOptions);
        var updatedAttachment = Assert.Single(attachments!);
        Assert.Equal(versionTwo.Id, updatedAttachment.BriefingVersionId);
        Assert.Equal(2, updatedAttachment.BriefingVersionNumber);

        var remove = await client.DeleteAsync(
            $"/api/companies/{company.Id}/briefings/{briefing.Id}/context-attachments?conversationId={conversation.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<ResearchContextAttachmentResponse[]>(
            $"/api/companies/{company.Id}/research-context-attachments?conversationId={conversation.Id}", JsonOptions))!);
    }

    [Fact]
    public async Task Research_rounds_keep_prior_search_and_crawl_evidence_in_the_final_context()
    {
        var ai = new SequencedAiProvider([
            """
            {"questionKind":"company_factual","facets":["awards by year"],"requestedYears":["2023","2024"],"query":"awards 2023","profileSourceDocumentIds":[],"answer":null,"followUpQuestion":null}
            """,
            """
            {"sufficient":false,"missingEvidence":["2024 award"],"nextQuery":"awards 2024","candidateIds":[]}
            """,
            """
            {"sufficient":true,"missingEvidence":[],"nextQuery":null,"candidateIds":[]}
            """,
            """
            {"status":"answered","answer":"Đã tìm thấy bằng chứng cho cả năm 2023 và 2024.","citedSourceDocumentIds":[],"citedWebEvidenceCandidateIds":["r1","r2"],"citedInvestigationIds":[],"claims":[{"text":"Có bằng chứng năm 2023.","evidenceIds":["web:r1"]},{"text":"Có bằng chứng năm 2024.","evidenceIds":["web:r2"]}],"limitations":[],"followUpQuestion":null}
            """
        ]);
        var search = new MultiRoundSearchProvider();
        var crawler = new MultiRoundCrawlerProvider();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiModelProvider>();
            services.AddScoped<IAiModelProvider>(_ => ai);
            services.RemoveAll<ISearchProvider>();
            services.AddScoped<ISearchProvider>(_ => search);
            services.RemoveAll<ICrawlerProvider>();
            services.AddScoped<ICrawlerProvider>(_ => crawler);
        }));
        var client = app.CreateClient();
        var company = await CreateCompanyAsync(client, $"FPT Multi Round {Guid.NewGuid():N}");
        await SeedProfileAsync(company.Id, 1, company.Name);
        var conversation = await CreateConversationAsync(client, company.Id);
        (await client.PatchAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/capabilities",
            new UpdateChatCapabilitiesRequest(true))).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages",
            new CreateChatMessageRequest("Các giải thưởng trong năm 2023 và 2024 là gì?"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, body);
        var message = JsonSerializer.Deserialize<SendChatMessageResponse>(body, JsonOptions)!;
        Assert.Equal(2, message.WebEvidenceSnapshots.Count);
        Assert.Equal(2, message.Citations.Count(item => item.Origin == ChatCitationOrigin.Web));
        Assert.Equal(2, search.Requests.Count);
        Assert.Equal(2, crawler.CallCount);
        Assert.Equal(4, ai.Requests.Count);
        Assert.Contains("Evidence for 2023", ai.Requests[^1].Prompt, StringComparison.Ordinal);
        Assert.Contains("Evidence for 2024", ai.Requests[^1].Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_final_after_model_budget_returns_insufficient_evidence_and_persists_tool_results()
    {
        var ai = new SequencedAiProvider([
            """
            {"questionKind":"company_factual","facets":["subsidiaries"],"requestedYears":[],"query":"Masan subsidiaries","profileSourceDocumentIds":[],"answer":null,"followUpQuestion":null}
            """,
            """
            {"sufficient":false,"missingEvidence":["another subsidiary source"],"nextQuery":"Masan subsidiaries annual report","candidateIds":[]}
            """,
            """
            {"sufficient":true,"missingEvidence":[],"nextQuery":null,"candidateIds":[]}
            """,
            """
            {"status":"answered","answer":"Unsupported citation.","citedSourceDocumentIds":[],"citedWebEvidenceCandidateIds":["r999"],"citedInvestigationIds":[],"claims":[{"text":"Unsupported.","evidenceIds":["web:r999"]}],"limitations":[],"followUpQuestion":null}
            """
        ]);
        var search = new MultiRoundSearchProvider();
        var crawler = new MultiRoundCrawlerProvider();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiModelProvider>();
            services.AddScoped<IAiModelProvider>(_ => ai);
            services.RemoveAll<ISearchProvider>();
            services.AddScoped<ISearchProvider>(_ => search);
            services.RemoveAll<ICrawlerProvider>();
            services.AddScoped<ICrawlerProvider>(_ => crawler);
        }));
        var client = app.CreateClient();
        var company = await CreateCompanyAsync(client, $"Masan Fallback {Guid.NewGuid():N}");
        await SeedProfileAsync(company.Id, 1, company.Name);
        var conversation = await CreateConversationAsync(client, company.Id);
        (await client.PatchAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/capabilities",
            new UpdateChatCapabilitiesRequest(true))).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages",
            new CreateChatMessageRequest("Masan có các công ty con nào?"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, body);
        var message = JsonSerializer.Deserialize<SendChatMessageResponse>(body, JsonOptions)!;
        Assert.Equal(ChatAnswerStatus.InsufficientEvidence, message.Status);
        Assert.Contains("chưa thể xác minh", message.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(message.ToolExecutions, item => item.Tool == "search_web");
        Assert.Contains(message.ToolExecutions, item => item.Tool == "read_web_page");
        Assert.Equal(2, message.WebEvidenceSnapshots.Count);
        Assert.Empty(message.Citations);
        Assert.Equal(4, ai.Requests.Count);
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
            using var document = JsonDocument.Parse(responses.Dequeue());
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
