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

public sealed partial class CompanyChatService
{
    private async Task<LoadedChatContexts> LoadResearchContextsAsync(Guid companyId, Guid conversationId, CancellationToken ct)
    {
        var attachments = await dbContext.ResearchContextAttachments.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.ConversationId == conversationId)
            .ToListAsync(ct);
        var selected = attachments.OrderByDescending(item => item.AttachedAt).Take(5).ToArray();
        var ids = selected.Where(item => item.InvestigationId.HasValue)
            .Select(item => item.InvestigationId!.Value).ToArray();
        var savedArtifactIds = selected.Where(item => item.SavedResearchArtifactId.HasValue)
            .Select(item => item.SavedResearchArtifactId!.Value).ToArray();
        var investigations = await dbContext.ManagedResearchInvestigations.AsNoTracking()
            .Where(item => item.CompanyId == companyId && ids.Contains(item.Id)).ToListAsync(ct);
        var savedArtifacts = await dbContext.SavedResearchArtifacts.AsNoTracking()
            .Where(item => item.CompanyId == companyId && savedArtifactIds.Contains(item.Id)).ToListAsync(ct);
        var versionIds = selected.Where(item => item.BriefingVersionId.HasValue)
            .Select(item => item.BriefingVersionId!.Value).ToArray();
        var versions = await dbContext.ResearchBriefingVersions.AsNoTracking()
            .Where(item => versionIds.Contains(item.Id)).ToListAsync(ct);
        var briefingIds = versions.Select(item => item.BriefingId).Distinct().ToArray();
        var ownedBriefingIds = await dbContext.ResearchBriefings.AsNoTracking()
            .Where(item => item.CompanyId == companyId && briefingIds.Contains(item.Id))
            .Select(item => item.Id).ToListAsync(ct);
        return new(
            investigations.Select(ToInvestigationContext).Concat(savedArtifacts.Select(ToInvestigationContext)).ToArray(),
            versions.Where(item => ownedBriefingIds.Contains(item.BriefingId)).Select(ToBriefingContext).ToArray());
    }

    private static ChatInvestigationContext ToInvestigationContext(ManagedResearchInvestigation item)
    {
        var objective = ChatText.Bound(item.Objective, 400);
        var summary = ChatText.Bound(item.Summary, 700);
        try
        {
            var result = JsonSerializer.Deserialize<ManagedResearchResult>(item.ResultJson, JsonOptions);
            if (result is not null)
            {
                var claims = string.Join('\n', result.Claims.Take(8).Select(claim =>
                    $"{claim.Topic}: {claim.Statement} [source leads: {string.Join(", ", claim.SupportingSourceUrls.Take(3))}]"));
                var sources = string.Join('\n', result.Sources.Take(8).Select(source => $"{source.Title}: {source.Url}"));
                var uncertainties = string.Join('\n', result.Uncertainties.Take(5));
                return new(item.Id, objective, summary, ChatText.Bound(
                    $"CLAIMS:\n{claims}\nSOURCE LEADS:\n{sources}\nUNCERTAINTIES:\n{uncertainties}", 3_000), item.CompletedAt);
            }
        }
        catch (JsonException) { /* Summary remains readable when old provider JSON is malformed. */ }
        return new(item.Id, objective, summary, string.Empty, item.CompletedAt);
    }

    private static ChatInvestigationContext ToInvestigationContext(
        Raven.Api.Features.Research.SavedArtifacts.SavedResearchArtifact item) =>
        new(item.Id, ChatText.Bound(item.Objective ?? item.Question, 400), ChatText.Bound(item.Summary, 700),
            ChatText.Bound(item.RawResponse ?? string.Empty, 3_000), item.CompletedAt ?? item.CreatedAt, item.Id);

    private static ChatBriefingContext ToBriefingContext(ResearchBriefingVersion item)
    {
        var sections = JsonSerializer.Deserialize<BriefingSection[]>(item.SectionsJson, JsonOptions) ?? [];
        var sources = JsonSerializer.Deserialize<BriefingSourceSnapshot[]>(item.SourcesJson, JsonOptions) ?? [];
        var sectionText = string.Join("\n\n", sections.Select(section =>
            $"## {section.Title}\n{string.Join('\n', section.Items.Take(20).Select(value => $"- {value}"))}\nSOURCE INVESTIGATION IDS: {string.Join(", ", section.SourceInvestigationIds)}"));
        var sourceText = string.Join('\n', sources.Take(12).Select(source =>
            $"- {source.InvestigationId} | {source.Title} | topics={string.Join(", ", source.Topics)} | materialUpdatedAt={source.MaterialUpdatedAt:O}" +
            (source.Uncertainties.Count > 0 ? $" | uncertainties={string.Join("; ", source.Uncertainties.Take(5))}" : string.Empty)));
        var material = ChatText.Bound($"SECTIONS:\n{sectionText}\n\nSOURCE INVESTIGATION METADATA:\n{sourceText}", 20_000);
        return new(item.BriefingId, item.Id, item.VersionNumber, item.Title, item.Template,
            ChatText.Bound(item.Objective, 2_000), material, item.GeneratedAt, item.ResearchThrough);
    }
}
