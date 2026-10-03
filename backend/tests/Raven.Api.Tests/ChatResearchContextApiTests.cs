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
            $"/api/companies/{company.Id}/investigations/{investigation.Id}/context-attachments",
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
            $"/api/companies/{other.Id}/investigations/{investigation.Id}/context-attachments",
            new AttachResearchContextRequest(otherConversation.Id));
        Assert.Equal(HttpStatusCode.NotFound, wrongCompany.StatusCode);
        var wrongConversation = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/investigations/{investigation.Id}/context-attachments",
            new AttachResearchContextRequest(otherConversation.Id));
        Assert.Equal(HttpStatusCode.NotFound, wrongConversation.StatusCode);
        var wrongList = await client.GetAsync(
            $"/api/companies/{company.Id}/research-context-attachments?conversationId={otherConversation.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrongList.StatusCode);
        var wrongRemoval = await client.DeleteAsync(
            $"/api/companies/{company.Id}/investigations/{investigation.Id}/context-attachments?conversationId={otherConversation.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrongRemoval.StatusCode);
    }

    [Fact]
    public async Task Saved_investigation_context_is_company_scoped_and_can_be_cited_in_chat()
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
        var artifact = new SavedResearchArtifact
        {
            CompanyId = company.Id,
            Title = "Current legal identity",
            Question = "What is the registered legal name?",
            Objective = "Verify the registered legal name",
            Summary = "The imported research reports the registered company name.",
            RawResponse = "An external assistant supplied this claim and an unverified registry link.",
            Origin = SavedResearchOrigin.ExternalImport,
            ResearchType = SavedResearchType.Deep,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
            db.SavedResearchArtifacts.Add(artifact);
            await db.SaveChangesAsync();
        }

        var attach = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/investigations/{artifact.Id}/context-attachments",
            new AttachResearchContextRequest(conversation.Id));
        Assert.Equal(HttpStatusCode.Created, attach.StatusCode);
        agent.Result = new ChatAgentResult(ChatAnswerStatus.Answered,
            "The attached investigation reports a registered company name.", [], null, [], [artifact.Id]);
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages",
            new CreateChatMessageRequest("What does the attached research report?"));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Contains(agent.LastRequest!.Investigations!, item => item.Id == artifact.Id &&
            item.Material.Contains("external assistant", StringComparison.OrdinalIgnoreCase));
        var restored = await client.GetFromJsonAsync<ChatConversationResponse>(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}", JsonOptions);
        var assistant = Assert.Single(restored!.Messages, item => item.Role == ChatMessageRole.Assistant);
        var citation = Assert.Single(assistant.Citations);
        Assert.Equal(ChatCitationOrigin.Investigation, citation.Origin);
        Assert.Equal(artifact.Id, citation.InvestigationId);

        var otherCompany = await CreateCompanyAsync(client);
        await SeedProfileAsync(otherCompany.Id, 1, "Other company profile");
        var otherConversation = await CreateConversationAsync(client, otherCompany.Id);
        var foreign = await client.PostAsJsonAsync(
            $"/api/companies/{otherCompany.Id}/investigations/{artifact.Id}/context-attachments",
            new AttachResearchContextRequest(otherConversation.Id));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
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
}
