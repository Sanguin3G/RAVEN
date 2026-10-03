using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Raven.Api.Data;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research;
using Raven.Api.Features.ManagedResearch;
using Raven.Api.Features.Research.Briefings;

namespace Raven.Api.Features.Chat;

public sealed partial class CompanyChatService(
    RavenDbContext dbContext,
    ICompanyProfilePersistenceService profiles,
    ICompanyChatAgentFactory agentFactory,
    IOptions<ChatResearchOptions> chatResearchOptions,
    ILogger<CompanyChatService> logger) : ICompanyChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };







    private static ChatProblemException Problem(int status, string code, string title, string detail) =>
        new(status, code, title, detail);

    private sealed record ValidatedChatResult(
        string Answer,
        ChatAnswerStatus Status,
        string? FollowUpQuestion,
        IReadOnlyList<ChatCitation> Citations,
        IReadOnlyList<ChatCitationResponse> CitationResponses,
        IReadOnlyList<string> WebCitationCandidateIds);
    private sealed record LoadedChatContexts(IReadOnlyList<ChatInvestigationContext> Investigations,
        IReadOnlyList<ChatBriefingContext> Briefings);
}
