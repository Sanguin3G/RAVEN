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
    public async Task Planner_runs_three_distinct_facet_queries_and_deduplicates_search_results_before_crawl()
    {
        var ai = new SequencedAiProvider([
            """
            {"questionKind":"company_factual","facets":["products","software","services"],"requestedYears":[],"queries":[{"facet":"products","query":"hardware product lines","sourcePreference":"company_primary"},{"facet":"software","query":"software platforms","sourcePreference":"company_primary"},{"facet":"services","query":"online services","sourcePreference":"neutral"}],"profileSourceDocumentIds":[],"answer":null,"followUpQuestion":null}
            """,
            """
            {"sufficient":true,"missingEvidence":[],"nextQuery":null,"candidateIds":[]}
            """,
            """
            {"status":"answered","answer":"The company has documented offerings.","citedSourceDocumentIds":[],"citedWebEvidenceCandidateIds":["__WEB_ID__"],"citedInvestigationIds":[],"citedBriefingVersionIds":[],"claims":[{"text":"Documented offerings.","evidenceIds":["web:__WEB_ID__"]}],"limitations":[],"followUpQuestion":null}
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
        var company = await CreateCompanyAsync(client, $"FPT Facets {Guid.NewGuid():N}");
        await SeedProfileAsync(company.Id, 1, company.Name);
        var conversation = await CreateConversationAsync(client, company.Id);
        (await client.PatchAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/capabilities",
            new UpdateChatCapabilitiesRequest(true))).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/chat/conversations/{conversation.Id}/messages",
            new CreateChatMessageRequest("Which products, software, and services does this company offer?"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var message = JsonSerializer.Deserialize<SendChatMessageResponse>(body, JsonOptions)!;
        Assert.Equal(ChatAnswerStatus.Answered, message.Status);
        Assert.Equal(3, search.Requests.Count);
        Assert.Equal(3, message.ToolExecutions.Count(item => item.Tool == "search_web"));
        Assert.All(search.Requests, item => Assert.Contains(company.Name, item.Query, StringComparison.OrdinalIgnoreCase));
        Assert.Single(message.WebEvidenceSnapshots);
        Assert.Single(message.Citations, citation => citation.Origin == ChatCitationOrigin.Web);
        Assert.Equal(1, crawler.CallCount);
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
}
