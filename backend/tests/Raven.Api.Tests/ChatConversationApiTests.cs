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
    public async Task Conversation_list_is_company_scoped_bounded_and_lightweight()
    {
        var client = factory.CreateClient();
        var company = await CreateCompanyAsync(client);
        var otherCompany = await CreateCompanyAsync(client);
        var profile = await SeedProfileAsync(company.Id, 1, "Company profile");
        await SeedProfileAsync(otherCompany.Id, 1, "Other profile");
        var older = await CreateConversationAsync(client, company.Id);
        var newer = await CreateConversationAsync(client, company.Id);
        await CreateConversationAsync(client, otherCompany.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
            var first = await db.ChatConversations.SingleAsync(item => item.Id == older.Id);
            first.Title = "Earlier question";
            first.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            var second = await db.ChatConversations.SingleAsync(item => item.Id == newer.Id);
            second.Title = "Recent question";
            second.UpdatedAt = DateTimeOffset.UtcNow;
            db.ChatMessages.Add(new ChatMessage { ConversationId = newer.Id, Role = ChatMessageRole.User,
                Content = "Recent question", Status = ChatMessageStatus.Completed });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{company.Id}/chat/conversations");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var summaries = await response.Content.ReadFromJsonAsync<ChatConversationSummary[]>(JsonOptions);
        Assert.NotNull(summaries);
        Assert.Equal(new[] { newer.Id, older.Id }, summaries.Select(item => item.Id));
        Assert.Equal("Recent question", summaries[0].Title);
        Assert.Equal(profile.Id, summaries[0].ProfileVersionId);
        Assert.Equal(1, summaries[0].MessageCount);
        Assert.DoesNotContain("Messages", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Delete_conversation_is_company_scoped_and_removes_only_its_chat_records()
    {
        var client = factory.CreateClient();
        var company = await CreateCompanyAsync(client);
        var otherCompany = await CreateCompanyAsync(client);
        await SeedProfileAsync(company.Id, 1, "Company profile");
        await SeedProfileAsync(otherCompany.Id, 1, "Other profile");
        var conversation = await CreateConversationAsync(client, company.Id);
        var otherConversation = await CreateConversationAsync(client, company.Id);
        var message = new ChatMessage
        {
            ConversationId = conversation.Id, Role = ChatMessageRole.Assistant,
            Content = "A sourced response", Status = ChatMessageStatus.Completed
        };
        var snapshot = new ChatWebEvidenceSnapshot
        {
            ChatMessageId = message.Id, Url = "https://example.com/news", NormalizedUrl = "https://example.com/news",
            ContentExcerpt = "Company news", SearchProvider = "test", SearchRank = 1
        };
        var briefing = new ResearchBriefing
        {
            CompanyId = company.Id, Title = "Hiring", Template = "Talent & Hiring", Objective = "Hiring signals"
        };
        var briefingVersion = new ResearchBriefingVersion
        {
            BriefingId = briefing.Id, VersionNumber = 1, GeneratedAt = DateTimeOffset.UtcNow,
            ResearchThrough = DateTimeOffset.UtcNow, Title = briefing.Title, Template = briefing.Template,
            Objective = briefing.Objective, SectionsJson = "[]", SourcesJson = "[]"
        };
        var removedAttachment = new ResearchContextAttachment
        {
            CompanyId = company.Id, ConversationId = conversation.Id,
            BriefingId = briefing.Id, BriefingVersionId = briefingVersion.Id
        };
        var retainedAttachment = new ResearchContextAttachment
        {
            CompanyId = company.Id, ConversationId = otherConversation.Id,
            BriefingId = briefing.Id, BriefingVersionId = briefingVersion.Id
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
            db.AddRange(message, snapshot, new ChatCitation
            {
                ChatMessageId = message.Id, WebEvidenceSnapshotId = snapshot.Id, Origin = ChatCitationOrigin.Web
            }, new ChatToolExecution
            {
                ChatMessageId = message.Id, Tool = "search", Provider = "test", Status = "Completed"
            }, briefing, briefingVersion, removedAttachment, retainedAttachment);
            await db.SaveChangesAsync();
        }

        var foreignDelete = await client.DeleteAsync($"/api/companies/{otherCompany.Id}/chat/conversations/{conversation.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        var delete = await client.DeleteAsync($"/api/companies/{company.Id}/chat/conversations/{conversation.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
            Assert.False(await db.ChatConversations.AnyAsync(item => item.Id == conversation.Id));
            Assert.True(await db.ChatConversations.AnyAsync(item => item.Id == otherConversation.Id));
            Assert.False(await db.ChatMessages.AnyAsync(item => item.Id == message.Id));
            Assert.False(await db.ChatCitations.AnyAsync(item => item.ChatMessageId == message.Id));
            Assert.False(await db.ChatWebEvidenceSnapshots.AnyAsync(item => item.ChatMessageId == message.Id));
            Assert.False(await db.ChatToolExecutions.AnyAsync(item => item.ChatMessageId == message.Id));
            Assert.False(await db.ResearchContextAttachments.AnyAsync(item => item.Id == removedAttachment.Id));
            Assert.True(await db.ResearchContextAttachments.AnyAsync(item => item.Id == retainedAttachment.Id));
            Assert.True(await db.ResearchBriefings.AnyAsync(item => item.Id == briefing.Id));
            Assert.True(await db.ResearchBriefingVersions.AnyAsync(item => item.Id == briefingVersion.Id));
        }
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
}
