using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Raven.Api.Data;
using Raven.Api.Features.Chat;
using Raven.Api.Features.Companies.Lifecycle;
using Raven.Api.Features.DeepResearch;
using Raven.Api.Features.ManagedResearch;
using Raven.Api.Features.Monitoring;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Settings;

namespace Raven.DemoSeed;

/// <summary>Builds a presentation database from a SQLite snapshot without writing to the source database.</summary>
public static partial class DemoDatabaseCurator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static RavenDbContext CreateContext(string path) => new(new DbContextOptionsBuilder<RavenDbContext>()
        .UseSqlite($"Data Source={path};Pooling=False")
        .Options);

    public static async Task ExportAsync(DemoSeedExportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var source = Path.GetFullPath(options.SourcePath);
        var manifestPath = Path.GetFullPath(options.ManifestPath);
        var output = Path.GetFullPath(options.OutputPath);
        var publicSeed = string.IsNullOrWhiteSpace(options.PublicSeedPath) ? null : Path.GetFullPath(options.PublicSeedPath);

        if (!File.Exists(source)) throw new FileNotFoundException("The source SQLite database was not found.", source);
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("The curation manifest was not found.", manifestPath);
        if (SamePath(source, output) || (publicSeed is not null && (SamePath(source, publicSeed) || SamePath(output, publicSeed))))
            throw new InvalidOperationException("Source, local demo, and public seed paths must all be different.");
        if (File.Exists(output)) throw new IOException($"Refusing to overwrite an existing database: {output}");
        if (publicSeed is not null && File.Exists(publicSeed)) throw new IOException($"Refusing to overwrite an existing database: {publicSeed}");

        var manifest = JsonSerializer.Deserialize<DemoSeedManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
            ?? throw new InvalidDataException("The curation manifest is empty or invalid.");
        ValidateManifest(manifest);

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (publicSeed is not null) Directory.CreateDirectory(Path.GetDirectoryName(publicSeed)!);
        var workingOutput = MakeTemporaryDatabasePath(output);
        var workingSeed = publicSeed is null ? null : MakeTemporaryDatabasePath(publicSeed);

        try
        {
            await BackupDatabaseAsync(source, workingOutput, cancellationToken);
            await using (var db = CreateContext(workingOutput))
            {
                await db.Database.MigrateAsync(cancellationToken);
                EnsureCurrentModel(db);
                await ValidateAllowlistExistsAsync(db, manifest, cancellationToken);
                await ValidateAllowedCompanyNamesAsync(db, manifest, cancellationToken);

                await ApplyArtifactExclusionsAsync(db, manifest, cancellationToken);
                await RemoveTransientChatConversationsAsync(db, cancellationToken);

                await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
                {
                    var allowlistedIds = manifest.Companies.Select(item => item.Id).ToHashSet();
                    var removeCompanyIds = await db.Companies.AsNoTracking()
                        .Where(company => !allowlistedIds.Contains(company.Id))
                        .Select(company => company.Id)
                        .ToListAsync(cancellationToken);
                    var deletion = new Raven.Api.Features.Companies.Lifecycle.CompanyDeletionService(db);
                    foreach (var companyId in removeCompanyIds)
                    {
                        await deletion.DeleteAsync(companyId, cancellationToken);
                    }

                    // Deployment identities and workspace overrides are never cloned from a developer database.
                    await db.ProviderCredentials.ExecuteDeleteAsync(cancellationToken);
                    await db.Users.ExecuteDeleteAsync(cancellationToken);
                    await db.Roles.ExecuteDeleteAsync(cancellationToken);

                    await CleanTransientStateAsync(db, cancellationToken);
                    await EnsureValidResearchSettingsAsync(db, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }

                await ValidateOutputAsync(db, manifest, cancellationToken);
            }

            await FinalizeSqliteFileAsync(workingOutput, cancellationToken);
            await ValidateSqliteIntegrityAsync(workingOutput, cancellationToken);
            await ValidateSensitiveStorageAsync(workingOutput, cancellationToken);

            if (workingSeed is not null)
            {
                await BackupDatabaseAsync(workingOutput, workingSeed, cancellationToken);
                await FinalizeSqliteFileAsync(workingSeed, cancellationToken);
                await using var seedDb = CreateContext(workingSeed);
                await ValidateOutputAsync(seedDb, manifest, cancellationToken);
                await ValidateSqliteIntegrityAsync(workingSeed, cancellationToken);
                await ValidateSensitiveStorageAsync(workingSeed, cancellationToken);
            }

            File.Move(workingOutput, output, overwrite: false);
            if (workingSeed is not null)
            {
                try
                {
                    File.Move(workingSeed, publicSeed!, overwrite: false);
                }
                catch
                {
                    File.Delete(output);
                    throw;
                }
            }

            Console.WriteLine($"Created curated demo database with {manifest.Companies.Count} Companies: {output}");
            if (publicSeed is not null) Console.WriteLine($"Created sanitized Cloud Run seed: {publicSeed}");
        }
        finally
        {
            DeleteSqliteFiles(workingOutput);
            if (workingSeed is not null) DeleteSqliteFiles(workingSeed);
        }
    }

    public static async Task BackupDatabaseAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        if (SamePath(source, destination)) throw new InvalidOperationException("A database cannot be backed up onto itself.");
        if (!File.Exists(source)) throw new FileNotFoundException("The source SQLite database was not found.", source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = source,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = destination,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        await using var sourceConnection = new SqliteConnection(sourceConnectionString);
        await sourceConnection.OpenAsync(cancellationToken);
        await using var destinationConnection = new SqliteConnection(destinationConnectionString);
        await destinationConnection.OpenAsync(cancellationToken);
        sourceConnection.BackupDatabase(destinationConnection);
    }

    public static void DeleteSqliteFiles(string path)
    {
        foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private static async Task ApplyArtifactExclusionsAsync(
        RavenDbContext db,
        DemoSeedManifest manifest,
        CancellationToken cancellationToken)
    {
        var allowedCompanyIds = manifest.Companies.Select(item => item.Id).ToArray();
        var chatService = new CompanyChatService(db, null!, null!, Options.Create(new ChatResearchOptions()),
            NullLogger<CompanyChatService>.Instance);

        foreach (var selection in manifest.Companies)
        {
            foreach (var conversationId in selection.ExcludeChatConversationIds ?? [])
            {
                if (!await db.ChatConversations.AnyAsync(item => item.Id == conversationId && item.CompanyId == selection.Id, cancellationToken))
                    throw new InvalidDataException($"Excluded chat conversation {conversationId} does not belong to selected Company {selection.Id}.");
                await chatService.DeleteConversationAsync(selection.Id, conversationId, cancellationToken);
            }
        }

        var requestedInvestigationIds = manifest.Companies.SelectMany(item => item.ExcludeInvestigationIds ?? []).Distinct().ToArray();
        var savedArtifacts = await db.SavedResearchArtifacts.AsNoTracking()
            .Where(item => requestedInvestigationIds.Contains(item.Id) && allowedCompanyIds.Contains(item.CompanyId))
            .Select(item => new { item.Id, item.CompanyId, item.ManagedResearchJobId }).ToListAsync(cancellationToken);
        var managedInvestigations = await db.ManagedResearchInvestigations.AsNoTracking()
            .Where(item => requestedInvestigationIds.Contains(item.Id) && allowedCompanyIds.Contains(item.CompanyId))
            .Select(item => new { item.Id, item.CompanyId, item.JobId, item.ConversationId, item.ChatMessageId }).ToListAsync(cancellationToken);
        var foundInvestigationIds = savedArtifacts.Select(item => item.Id).Concat(managedInvestigations.Select(item => item.Id)).ToHashSet();
        var expectedInvestigationIds = manifest.Companies.SelectMany(item => item.ExcludeInvestigationIds ?? []).ToHashSet();
        if (!foundInvestigationIds.SetEquals(expectedInvestigationIds))
            throw new InvalidDataException("An excluded Investigation ID is missing or belongs to a non-selected Company.");
        foreach (var selection in manifest.Companies)
        {
            foreach (var investigationId in selection.ExcludeInvestigationIds ?? [])
            {
                if (!savedArtifacts.Any(item => item.Id == investigationId && item.CompanyId == selection.Id)
                    && !managedInvestigations.Any(item => item.Id == investigationId && item.CompanyId == selection.Id))
                    throw new InvalidDataException($"Excluded Investigation {investigationId} does not belong to selected Company {selection.Id}.");
            }
        }

        var requestedBriefingIds = manifest.Companies.SelectMany(item => item.ExcludeBriefingIds ?? []).Distinct().ToArray();
        var explicitBriefings = await db.ResearchBriefings.AsNoTracking()
            .Where(item => requestedBriefingIds.Contains(item.Id) && allowedCompanyIds.Contains(item.CompanyId))
            .Select(item => new { item.Id, item.CompanyId }).ToListAsync(cancellationToken);
        if (explicitBriefings.Count != requestedBriefingIds.Length)
            throw new InvalidDataException("An excluded Briefing ID is missing or belongs to a non-selected Company.");
        foreach (var selection in manifest.Companies)
        {
            foreach (var briefingId in selection.ExcludeBriefingIds ?? [])
            {
                if (!explicitBriefings.Any(item => item.Id == briefingId && item.CompanyId == selection.Id))
                    throw new InvalidDataException($"Excluded Briefing {briefingId} does not belong to selected Company {selection.Id}.");
            }
        }

        // A Briefing embeds immutable Investigation snapshots; exclude it whole rather than leave material from an excluded Investigation behind.
        var excludedInvestigationIdStrings = requestedInvestigationIds.Select(id => id.ToString()).ToArray();
        var affectedBriefingIds = new HashSet<Guid>();
        if (excludedInvestigationIdStrings.Length > 0)
        {
            var versions = await db.ResearchBriefingVersions.AsNoTracking()
                .Select(item => new { item.BriefingId, item.SourcesJson }).ToListAsync(cancellationToken);
            foreach (var version in versions)
                if (excludedInvestigationIdStrings.Any(id => version.SourcesJson.Contains(id, StringComparison.OrdinalIgnoreCase)))
                    affectedBriefingIds.Add(version.BriefingId);
        }
        var allExcludedBriefingIds = requestedBriefingIds.Concat(affectedBriefingIds).Distinct().ToArray();

        var managedJobIds = managedInvestigations.Select(item => item.JobId)
            .Concat(savedArtifacts.Select(item => Guid.TryParse(item.ManagedResearchJobId, out var id) ? id : Guid.Empty))
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        var relatedJobs = await db.ManagedResearchJobs.AsNoTracking().Where(item => managedJobIds.Contains(item.Id))
            .Select(item => new { item.CompanyId, item.ConversationId, item.ChatMessageId }).ToListAsync(cancellationToken);
        var relatedConversations = new HashSet<(Guid CompanyId, Guid ConversationId)>();

        foreach (var artifact in savedArtifacts)
        {
            var attachments = await db.ResearchContextAttachments.AsNoTracking().Where(item => item.SavedResearchArtifactId == artifact.Id)
                .Select(item => new { item.CompanyId, item.ConversationId }).ToListAsync(cancellationToken);
            foreach (var item in attachments) relatedConversations.Add((item.CompanyId, item.ConversationId));
            var citations = await db.ChatCitations.AsNoTracking().Where(item => item.SavedResearchArtifactId == artifact.Id)
                .Select(item => new { item.ChatMessage.ConversationId, item.ChatMessage.Conversation.CompanyId }).ToListAsync(cancellationToken);
            foreach (var item in citations) relatedConversations.Add((item.CompanyId, item.ConversationId));
        }
        foreach (var investigation in managedInvestigations)
        {
            if (investigation.ConversationId is not null) relatedConversations.Add((investigation.CompanyId, investigation.ConversationId.Value));
            if (investigation.ChatMessageId is not null)
            {
                var linked = await db.ChatMessages.AsNoTracking().Where(item => item.Id == investigation.ChatMessageId)
                    .Select(item => new { item.ConversationId, item.Conversation.CompanyId }).SingleOrDefaultAsync(cancellationToken);
                if (linked is not null) relatedConversations.Add((linked.CompanyId, linked.ConversationId));
            }
            var attachments = await db.ResearchContextAttachments.AsNoTracking().Where(item => item.InvestigationId == investigation.Id)
                .Select(item => new { item.CompanyId, item.ConversationId }).ToListAsync(cancellationToken);
            foreach (var item in attachments) relatedConversations.Add((item.CompanyId, item.ConversationId));
            var citations = await db.ChatCitations.AsNoTracking().Where(item => item.InvestigationId == investigation.Id)
                .Select(item => new { item.ChatMessage.ConversationId, item.ChatMessage.Conversation.CompanyId }).ToListAsync(cancellationToken);
            foreach (var item in citations) relatedConversations.Add((item.CompanyId, item.ConversationId));
        }
        foreach (var job in relatedJobs)
        {
            if (job.ConversationId is not null) relatedConversations.Add((job.CompanyId, job.ConversationId.Value));
            if (job.ChatMessageId is not null)
            {
                var linked = await db.ChatMessages.AsNoTracking().Where(item => item.Id == job.ChatMessageId)
                    .Select(item => new { item.ConversationId, item.Conversation.CompanyId }).SingleOrDefaultAsync(cancellationToken);
                if (linked is not null) relatedConversations.Add((linked.CompanyId, linked.ConversationId));
            }
        }
        foreach (var briefingId in allExcludedBriefingIds)
        {
            var briefing = await db.ResearchBriefings.AsNoTracking()
                .Where(item => item.Id == briefingId && allowedCompanyIds.Contains(item.CompanyId))
                .Select(item => new { item.CompanyId }).SingleOrDefaultAsync(cancellationToken);
            if (briefing is null)
            {
                if (requestedBriefingIds.Contains(briefingId)) throw new InvalidDataException("An excluded Briefing is not available to the selected Companies.");
                continue;
            }
            var attachments = await db.ResearchContextAttachments.AsNoTracking().Where(item => item.BriefingId == briefingId)
                .Select(item => new { item.CompanyId, item.ConversationId }).ToListAsync(cancellationToken);
            foreach (var item in attachments) relatedConversations.Add((item.CompanyId, item.ConversationId));
            var versionIds = await db.ResearchBriefingVersions.AsNoTracking().Where(item => item.BriefingId == briefingId)
                .Select(item => item.Id).ToArrayAsync(cancellationToken);
            var citations = await db.ChatCitations.AsNoTracking()
                .Where(item => item.BriefingVersionId != null && versionIds.Contains(item.BriefingVersionId.Value))
                .Select(item => new { item.ChatMessage.ConversationId, item.ChatMessage.Conversation.CompanyId }).ToListAsync(cancellationToken);
            foreach (var item in citations) relatedConversations.Add((item.CompanyId, item.ConversationId));
        }

        foreach (var conversation in relatedConversations)
            if (allowedCompanyIds.Contains(conversation.CompanyId))
                await chatService.DeleteConversationAsync(conversation.CompanyId, conversation.ConversationId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var artifact in savedArtifacts)
        {
            await db.ResearchContextAttachments.Where(item => item.SavedResearchArtifactId == artifact.Id).ExecuteDeleteAsync(cancellationToken);
            await db.InvestigationReviewStates.Where(item => item.CompanyId == artifact.CompanyId && item.MaterialId == artifact.Id).ExecuteDeleteAsync(cancellationToken);
            await db.SavedResearchArtifacts.Where(item => item.Id == artifact.Id).ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var investigation in managedInvestigations)
        {
            await db.ResearchContextAttachments.Where(item => item.InvestigationId == investigation.Id).ExecuteDeleteAsync(cancellationToken);
            await db.InvestigationReviewStates.Where(item => item.CompanyId == investigation.CompanyId && item.MaterialId == investigation.Id).ExecuteDeleteAsync(cancellationToken);
            await db.ManagedResearchInvestigations.Where(item => item.Id == investigation.Id).ExecuteDeleteAsync(cancellationToken);
        }
        if (managedJobIds.Length > 0)
            await db.ManagedResearchJobs.Where(item => managedJobIds.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);

        foreach (var briefingId in allExcludedBriefingIds)
        {
            await db.ResearchContextAttachments.Where(item => item.BriefingId == briefingId).ExecuteDeleteAsync(cancellationToken);
            await db.BriefingGenerationJobs.Where(item => item.BriefingId == briefingId || item.ResultBriefingId == briefingId).ExecuteDeleteAsync(cancellationToken);
            await db.ResearchBriefingVersions.Where(item => item.BriefingId == briefingId).ExecuteDeleteAsync(cancellationToken);
            await db.ResearchBriefings.Where(item => item.Id == briefingId).ExecuteDeleteAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RemoveTransientChatConversationsAsync(RavenDbContext db, CancellationToken cancellationToken)
    {
        var activeJobIds = await db.ManagedResearchJobs.AsNoTracking()
            .Where(item => item.Status == ManagedResearchJobStatus.Queued || item.Status == ManagedResearchJobStatus.Researching)
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        var conversationIds = await db.ChatMessages.AsNoTracking()
            .Where(item => item.Status == ChatMessageStatus.Pending ||
                (item.ManagedResearchJobId.HasValue && activeJobIds.Contains(item.ManagedResearchJobId.Value)))
            .Select(item => new { item.Conversation.CompanyId, item.ConversationId }).Distinct().ToListAsync(cancellationToken);
        var activeJobConversations = await db.ManagedResearchJobs.AsNoTracking()
            .Where(item => item.Status == ManagedResearchJobStatus.Queued || item.Status == ManagedResearchJobStatus.Researching)
            .Where(item => item.ConversationId != null)
            .Select(item => new { item.CompanyId, ConversationId = item.ConversationId!.Value }).ToListAsync(cancellationToken);
        var chatService = new CompanyChatService(db, null!, null!, Options.Create(new ChatResearchOptions()),
            NullLogger<CompanyChatService>.Instance);
        foreach (var item in conversationIds.Concat(activeJobConversations).Distinct())
            await chatService.DeleteConversationAsync(item.CompanyId, item.ConversationId, cancellationToken);
    }

    private static async Task CleanTransientStateAsync(RavenDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var activeResearch = await db.ResearchRuns
            .Where(item => item.Status == ResearchRunStatus.Searching || item.Status == ResearchRunStatus.Crawling)
            .ToListAsync(cancellationToken);
        foreach (var run in activeResearch)
        {
            run.Status = ResearchRunStatus.Cancelled;
            run.Stage = ResearchStage.Cancelled;
            run.CompletedAt = now;
            run.Error = null;
        }

        var activeDeepRunIds = await db.DeepResearchRuns.AsNoTracking()
            .Where(item => item.Status == DeepResearchRunStatus.Queued || item.Status == DeepResearchRunStatus.Running)
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        if (activeDeepRunIds.Length > 0)
        {
            await db.DeepResearchActivities.Where(item => activeDeepRunIds.Contains(item.DeepResearchRunId)).ExecuteDeleteAsync(cancellationToken);
            await db.DeepResearchRuns.Where(item => activeDeepRunIds.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
        }
        await db.DeepResearchActivities.Where(item => item.Status == DeepResearchActivityStatus.Working).ExecuteDeleteAsync(cancellationToken);

        var activeManagedJobs = await db.ManagedResearchJobs
            .Where(item => item.Status == ManagedResearchJobStatus.Queued || item.Status == ManagedResearchJobStatus.Researching)
            .ToListAsync(cancellationToken);
        foreach (var job in activeManagedJobs) job.Cancel(now);

        // Generation requests and external analysis are temporary workflow state; completed Briefing versions and saved Investigations remain.
        await db.BriefingGenerationJobs.ExecuteDeleteAsync(cancellationToken);
        await db.ExternalResearchAnalysisJobs.ExecuteDeleteAsync(cancellationToken);
        await db.WorkspaceResearchReviewStates.ExecuteDeleteAsync(cancellationToken);
        await db.ResearchEvents.ExecuteDeleteAsync(cancellationToken);

        var activeMonitoring = await db.CompanyMonitoringSettings
            .Where(item => item.LastRunStatus == MonitoringRunStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var setting in activeMonitoring)
        {
            setting.Enabled = false;
            setting.LastRunStatus = MonitoringRunStatus.Cancelled;
            setting.ActiveClaimId = null;
            setting.ClaimExpiresAt = null;
            setting.UpdatedAt = now;
        }
    }

    private static async Task EnsureValidResearchSettingsAsync(RavenDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.ResearchSettings.AnyAsync(cancellationToken))
            db.ResearchSettings.Add(ResearchSettingsDefaults.CreateEntity(DateTimeOffset.UnixEpoch));
        await db.SaveChangesAsync(cancellationToken);
        await new ResearchSettingsService(new EfResearchSettingsStore(db)).GetAsync(cancellationToken);
        if (await db.ResearchSettings.CountAsync(cancellationToken) != 1)
            throw new InvalidDataException("The curated database must contain exactly one valid Research Settings row.");
    }

    private static async Task ValidateOutputAsync(RavenDbContext db, DemoSeedManifest manifest, CancellationToken cancellationToken)
    {
        var allowed = manifest.Companies.Select(item => item.Id).ToHashSet();
        var companyIds = await db.Companies.AsNoTracking().Select(item => item.Id).ToListAsync(cancellationToken);
        if (!allowed.SetEquals(companyIds)) throw new InvalidDataException("The output contains Companies outside the explicit allowlist or is missing an approved Company.");
        await ValidateAllowedCompanyNamesAsync(db, manifest, cancellationToken);
        if (await db.ProviderCredentials.AnyAsync(cancellationToken)) throw new InvalidDataException("Provider credential records remain in the output.");
        if (await db.Users.AnyAsync(cancellationToken)) throw new InvalidDataException("Identity user records remain in the output.");
        if (await db.ResearchSettings.CountAsync(cancellationToken) != 1) throw new InvalidDataException("Research Settings must contain exactly one row.");
        if (await db.ResearchRuns.AnyAsync(item => item.Status == ResearchRunStatus.Searching || item.Status == ResearchRunStatus.Crawling, cancellationToken))
            throw new InvalidDataException("An active Native Research run remains in the output.");
        if (await db.DeepResearchRuns.AnyAsync(item => item.Status == DeepResearchRunStatus.Queued || item.Status == DeepResearchRunStatus.Running, cancellationToken))
            throw new InvalidDataException("An active Deep Research run remains in the output.");
        if (await db.ManagedResearchJobs.AnyAsync(item => item.Status == ManagedResearchJobStatus.Queued || item.Status == ManagedResearchJobStatus.Researching, cancellationToken))
            throw new InvalidDataException("An active Managed Research job remains in the output.");
        if (await db.BriefingGenerationJobs.AnyAsync(cancellationToken)) throw new InvalidDataException("Briefing generation request state remains in the output.");
        if (await db.ExternalResearchAnalysisJobs.AnyAsync(cancellationToken)) throw new InvalidDataException("External analysis job state remains in the output.");
        if (await db.ChatMessages.AnyAsync(item => item.Status == ChatMessageStatus.Pending, cancellationToken))
            throw new InvalidDataException("An incomplete Chat turn remains in the output.");
        if (await db.DeepResearchActivities.AnyAsync(item => item.Status == DeepResearchActivityStatus.Working, cancellationToken))
            throw new InvalidDataException("A working Deep Research activity remains in the output.");
        if (await db.CompanyMonitoringSettings.AnyAsync(item => item.LastRunStatus == MonitoringRunStatus.Running, cancellationToken))
            throw new InvalidDataException("An active Monitoring run remains in the output.");
        var pendingMigrations = await db.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any()) throw new InvalidDataException("The output is missing one or more current EF migrations.");
        EnsureCurrentModel(db);
    }

    private static async Task ValidateAllowedCompanyNamesAsync(RavenDbContext db, DemoSeedManifest manifest, CancellationToken cancellationToken)
    {
        var ids = manifest.Companies.Select(selection => selection.Id).ToArray();
        var names = await db.Companies.AsNoTracking().Where(item => ids.Contains(item.Id))
            .Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken);
        var forbidden = ForbiddenCompanyNamePattern();
        var blocked = names.FirstOrDefault(item => forbidden.IsMatch(item.Name));
        if (blocked is not null)
            throw new InvalidDataException($"Company {blocked.Id} has a name matching a forbidden demo-data category; remove it from the manifest.");
    }

    private static async Task ValidateAllowlistExistsAsync(RavenDbContext db, DemoSeedManifest manifest, CancellationToken cancellationToken)
    {
        var allowed = manifest.Companies.Select(item => item.Id).ToHashSet();
        var found = await db.Companies.AsNoTracking().Where(item => allowed.Contains(item.Id)).Select(item => item.Id).ToListAsync(cancellationToken);
        if (!allowed.SetEquals(found)) throw new InvalidDataException("At least one allowlisted Company ID does not exist in the source workspace.");
    }

    private static void EnsureCurrentModel(RavenDbContext db)
    {
        if (db.Database.HasPendingModelChanges())
            throw new InvalidDataException("The current EF model has pending changes; create and apply migrations before freezing demo data.");
    }

    private static void ValidateManifest(DemoSeedManifest manifest)
    {
        if (manifest.Companies is null || manifest.Companies.Count == 0)
            throw new InvalidDataException("The manifest must explicitly allowlist at least one Company ID.");
        if (manifest.Companies.Any(item => item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Role) || item.Role.Length > 40))
            throw new InvalidDataException("Every selected Company needs a non-empty ID and a short presentation role.");
        if (manifest.Companies.Select(item => item.Id).Distinct().Count() != manifest.Companies.Count)
            throw new InvalidDataException("Company IDs in the manifest must be unique.");
        var childIds = manifest.Companies.SelectMany(item => (item.ExcludeChatConversationIds ?? [])
            .Concat(item.ExcludeInvestigationIds ?? []).Concat(item.ExcludeBriefingIds ?? [])).ToArray();
        if (childIds.Any(item => item == Guid.Empty) || childIds.Distinct().Count() != childIds.Length)
            throw new InvalidDataException("Excluded artifact IDs must be non-empty and unique within the manifest.");
    }

    private static async Task FinalizeSqliteFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.CommandText = "PRAGMA journal_mode=DELETE";
        var journalMode = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (!string.Equals(journalMode, "delete", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Could not finalize the database as a standalone SQLite file.");
        // Deleted rows can remain in free pages. Rebuild before distributing an artifact.
        command.CommandText = "VACUUM";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ValidateSqliteIntegrityAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check";
            var result = (string?)await integrity.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PRAGMA integrity_check did not report ok.");
        }
        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_key_check";
            await using var reader = await foreignKeys.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                throw new InvalidDataException("PRAGMA foreign_key_check found one or more violations.");
        }
    }

    private static async Task ValidateSensitiveStorageAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        var tables = new List<string>();
        await using (var tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            await using var reader = await tableCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) tables.Add(reader.GetString(0));
        }

        foreach (var table in tables)
        {
            if (table.StartsWith("AspNet", StringComparison.OrdinalIgnoreCase))
            {
                await using var countCommand = connection.CreateCommand();
                countCommand.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(table)}";
                if (Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken)) != 0)
                    throw new InvalidDataException("Identity account, role, claim, login, token, or session state remains in the output.");
            }

            var sensitiveColumns = new List<string>();
            await using (var columnsCommand = connection.CreateCommand())
            {
                columnsCommand.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)})";
                await using var reader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var column = reader.GetString(1);
                    if (SensitiveColumnPattern().IsMatch(column)) sensitiveColumns.Add(column);
                }
            }

            foreach (var column in sensitiveColumns)
            {
                await using var valueCommand = connection.CreateCommand();
                valueCommand.CommandText = $"SELECT {QuoteIdentifier(column)} FROM {QuoteIdentifier(table)} WHERE {QuoteIdentifier(column)} IS NOT NULL";
                await using var reader = await valueCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var value = reader.GetValue(0);
                    if (value is byte[] bytes && bytes.Length > 0 || value is string text && !string.IsNullOrWhiteSpace(text))
                        throw new InvalidDataException($"Sensitive data remains in database column {table}.{column}; inspect only the local temporary copy.");
                }
            }
        }
    }

    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string MakeTemporaryDatabasePath(string finalPath) =>
        Path.Combine(Path.GetDirectoryName(finalPath)!, $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp.db");

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?i)(?:\bpornhub\b|\badult\b|\bjoke\b|\bdebug\b|\btest\b|\bpractice\b|\bthrowaway\b)")]
    private static partial Regex ForbiddenCompanyNamePattern();

    [GeneratedRegex(@"(?i)(?:api[_-]?key|password|secret|credential|ciphertext|nonce|authentication.?tag|access.?token|refresh.?token|session|cookie)")]
    private static partial Regex SensitiveColumnPattern();
}
