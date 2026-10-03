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
}
