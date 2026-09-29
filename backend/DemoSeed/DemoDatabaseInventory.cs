using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Chat;
using Raven.Api.Features.Research;

namespace Raven.DemoSeed;

public sealed record DemoDatabaseInventory(IReadOnlyList<DemoCompanyInventory> Companies,
    IReadOnlyList<PotentialDuplicatePair> PotentialDuplicates);

public sealed record DemoCompanyInventory(
    Guid Id,
    string CompanyName,
    bool HasAcceptedProfile,
    int ProfileVersionCount,
    int SourceCount,
    int SuccessfulResearchCount,
    int FailedResearchCount,
    int InvestigationCount,
    int BriefingCount,
    int BriefingVersionCount,
    int ChatConversationCount,
    bool MonitoringConfigured,
    bool Archived,
    int WorkspaceReviewStateCount,
    IReadOnlyList<DemoInvestigationInventory> Investigations,
    IReadOnlyList<DemoBriefingInventory> Briefings,
    IReadOnlyList<DemoChatInventory> Chats);

public sealed record DemoInvestigationInventory(Guid Id, string Kind, DateTimeOffset? CompletedAt);
public sealed record DemoBriefingInventory(Guid Id, string Title, int VersionCount, DateTimeOffset UpdatedAt);
public sealed record DemoChatInventory(Guid Id, DateTimeOffset UpdatedAt, int MessageCount, int CompletedAssistantMessageCount);
public sealed record PotentialDuplicatePair(Guid FirstCompanyId, Guid SecondCompanyId, string Reason);

public static class DemoDatabaseInventoryReader
{
    public static async Task<DemoDatabaseInventory> ReadAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("The source SQLite database was not found.", source);

        var temporary = Path.Combine(Path.GetTempPath(), $"raven-inventory-{Guid.NewGuid():N}.db");
        try
        {
            await DemoDatabaseCurator.BackupDatabaseAsync(source, temporary, cancellationToken);
            await using var db = DemoDatabaseCurator.CreateContext(temporary);
            await db.Database.MigrateAsync(cancellationToken);

            var companies = await db.Companies.AsNoTracking().OrderBy(item => item.Name)
                .Select(item => new CompanyInventorySource(item.Id, item.Name, item.LegalName, item.RegistrationNumber,
                    item.Website, item.ArchivedAt != null))
                .ToListAsync(cancellationToken);
            var profileCounts = await db.CompanyProfileVersions.AsNoTracking().GroupBy(item => item.CompanyId)
                .Select(group => new { CompanyId = group.Key, Count = group.Count() }).ToDictionaryAsync(item => item.CompanyId, item => item.Count, cancellationToken);
            var sourceCounts = await db.SourceDocuments.AsNoTracking().GroupBy(item => item.CompanyId)
                .Select(group => new { CompanyId = group.Key, Count = group.Count() }).ToDictionaryAsync(item => item.CompanyId, item => item.Count, cancellationToken);
            var runs = await db.ResearchRuns.AsNoTracking().Select(item => new { item.CompanyId, item.Status }).ToListAsync(cancellationToken);
            var savedInvestigations = await db.SavedResearchArtifacts.AsNoTracking()
                .Select(item => new { item.Id, item.CompanyId, item.CompletedAt }).ToListAsync(cancellationToken);
            var managedInvestigations = await db.ManagedResearchInvestigations.AsNoTracking()
                .Select(item => new { item.Id, item.CompanyId, item.CompletedAt }).ToListAsync(cancellationToken);
            var briefings = await db.ResearchBriefings.AsNoTracking()
                .Select(item => new { item.Id, item.CompanyId, item.Title, item.UpdatedAt }).ToListAsync(cancellationToken);
            var briefingVersions = await db.ResearchBriefingVersions.AsNoTracking().Select(item => item.BriefingId).ToListAsync(cancellationToken);
            var chats = await db.ChatConversations.AsNoTracking().Select(item => new
            {
                item.Id,
                item.CompanyId,
                item.UpdatedAt,
                MessageCount = item.Messages.Count,
                CompletedAssistantMessageCount = item.Messages.Count(message =>
                    message.Role == ChatMessageRole.Assistant && message.Status == ChatMessageStatus.Completed && message.Content != "")
            }).ToListAsync(cancellationToken);
            var monitoringCompanyIds = await db.CompanyMonitoringSettings.AsNoTracking().Select(item => item.CompanyId).ToListAsync(cancellationToken);
            var reviewStateCounts = await db.WorkspaceResearchReviewStates.AsNoTracking().GroupBy(item => item.CompanyId)
                .Select(group => new { CompanyId = group.Key, Count = group.Count() }).ToDictionaryAsync(item => item.CompanyId, item => item.Count, cancellationToken);

            var result = companies.Select(company =>
            {
                var companyBriefings = briefings.Where(item => item.CompanyId == company.Id)
                    .Select(item => new DemoBriefingInventory(item.Id, item.Title,
                        briefingVersions.Count(briefingId => briefingId == item.Id), item.UpdatedAt))
                    .OrderByDescending(item => item.UpdatedAt).ToArray();
                var companyInvestigations = savedInvestigations.Where(item => item.CompanyId == company.Id)
                    .Select(item => new DemoInvestigationInventory(item.Id, "saved", item.CompletedAt))
                    .Concat(managedInvestigations.Where(item => item.CompanyId == company.Id)
                        .Select(item => new DemoInvestigationInventory(item.Id, "managed", item.CompletedAt)))
                    .OrderByDescending(item => item.CompletedAt).ToArray();
                var companyChats = chats.Where(item => item.CompanyId == company.Id)
                    .Select(item => new DemoChatInventory(item.Id, item.UpdatedAt, item.MessageCount, item.CompletedAssistantMessageCount))
                    .OrderByDescending(item => item.UpdatedAt).ToArray();
                var companyRuns = runs.Where(item => item.CompanyId == company.Id).ToArray();

                return new DemoCompanyInventory(
                    company.Id,
                    company.Name,
                    profileCounts.GetValueOrDefault(company.Id) > 0,
                    profileCounts.GetValueOrDefault(company.Id),
                    sourceCounts.GetValueOrDefault(company.Id),
                    companyRuns.Count(item => item.Status == ResearchRunStatus.Completed),
                    companyRuns.Count(item => item.Status == ResearchRunStatus.Failed),
                    companyInvestigations.Length,
                    companyBriefings.Length,
                    companyBriefings.Sum(item => item.VersionCount),
                    companyChats.Length,
                    monitoringCompanyIds.Contains(company.Id),
                    company.Archived,
                    reviewStateCounts.GetValueOrDefault(company.Id),
                    companyInvestigations,
                    companyBriefings,
                    companyChats);
            }).ToArray();

            return new DemoDatabaseInventory(result, BuildPotentialDuplicates(companies));
        }
        finally
        {
            DemoDatabaseCurator.DeleteSqliteFiles(temporary);
        }
    }

    private static IReadOnlyList<PotentialDuplicatePair> BuildPotentialDuplicates(IReadOnlyList<CompanyInventorySource> companies)
    {
        var pairs = new Dictionary<(Guid, Guid), HashSet<string>>();
        foreach (var left in companies)
        foreach (var right in companies.Where(item => item.Id.CompareTo(left.Id) > 0))
        {
            var reasons = new HashSet<string>(StringComparer.Ordinal);
            var leftRegistration = NormalizeIdentifier(left.RegistrationNumber);
            var rightRegistration = NormalizeIdentifier(right.RegistrationNumber);
            if (leftRegistration.Length > 0 && leftRegistration == rightRegistration) reasons.Add("same registration number");

            var leftWebsite = NormalizeWebsite(left.Website);
            var rightWebsite = NormalizeWebsite(right.Website);
            if (leftWebsite.Length > 0 && leftWebsite == rightWebsite) reasons.Add("same website host");

            var leftName = NormalizeIdentifier(left.LegalName ?? left.Name);
            var rightName = NormalizeIdentifier(right.LegalName ?? right.Name);
            if (leftName.Length > 0 && leftName == rightName) reasons.Add("same normalized name");
            if (reasons.Count > 0) pairs[(left.Id, right.Id)] = reasons;
        }

        return pairs.Select(pair => new PotentialDuplicatePair(pair.Key.Item1, pair.Key.Item2,
                string.Join("; ", pair.Value.OrderBy(reason => reason, StringComparer.Ordinal))))
            .ToArray();
    }

    private static string NormalizeIdentifier(string? value) =>
        string.Concat((value ?? "").Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static string NormalizeWebsite(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return "";
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    private sealed record CompanyInventorySource(Guid Id, string Name, string? LegalName, string? RegistrationNumber,
        string? Website, bool Archived);
}
