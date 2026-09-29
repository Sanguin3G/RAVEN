using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Auth;
using Raven.Api.Features.Chat;
using Raven.Api.Features.Companies;
using Raven.Api.Features.DeepResearch;
using Raven.Api.Features.ProviderCredentials;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Briefings;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.ExternalImport;
using Raven.Api.Features.Research.Sources;
using Raven.DemoSeed;

namespace Raven.Api.Tests;

public sealed class DemoDatabaseCuratorTests
{
    [Fact]
    public async Task Export_keeps_allowlisted_graph_prunes_exclusions_and_security_state_without_touching_source()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"raven-demo-curator-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.db");
        var output = Path.Combine(directory, "raven.demo.db");
        var manifestPath = Path.Combine(directory, "manifest.json");
        var approved = NewCompany("Approved Showcase Ltd");
        var excluded = NewCompany("Temporary practice record");
        var sourceDocumentId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var goodConversationId = Guid.NewGuid();
        var excludedConversationId = Guid.NewGuid();

        try
        {
            await using (var db = DemoDatabaseCurator.CreateContext(source))
            {
                await db.Database.MigrateAsync();
                var approvedRun = NewResearchRun(approved, ResearchRunStatus.Searching);
                var approvedSource = NewSource(approved, approvedRun, sourceDocumentId);
                var acceptedProfile = new CompanyProfileVersion
                {
                    Id = profileId,
                    CompanyId = approved.Id,
                    ResearchRunId = approvedRun.Id,
                    Version = 1,
                    GeneratedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    ConfirmedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                    DisplayName = approved.Name,
                    Summary = "A source-backed accepted profile.",
                };
                acceptedProfile.ProfileJson = JsonSerializer.Serialize(acceptedProfile, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var goodChat = NewConversation(approved, acceptedProfile, goodConversationId);
                var excludedChat = NewConversation(approved, acceptedProfile, excludedConversationId);
                var excludedRun = NewResearchRun(excluded, ResearchRunStatus.Completed);
                var excludedSource = NewSource(excluded, excludedRun, Guid.NewGuid());

                var profileEvidence = new ProfileEvidence
                {
                    CompanyProfileVersionId = acceptedProfile.Id,
                    FieldPath = "summary",
                    SourceDocumentIdsJson = JsonSerializer.Serialize(new[] { approvedSource.Id })
                };
                profileEvidence.SourceDocumentIds.Add(approvedSource.Id);

                db.Companies.AddRange(approved, excluded);
                db.ResearchRuns.AddRange(approvedRun, excludedRun);
                db.SourceDocuments.AddRange(approvedSource, excludedSource);
                db.CompanyProfileVersions.Add(acceptedProfile);
                db.ProfileEvidences.Add(profileEvidence);
                db.ChatConversations.AddRange(goodChat, excludedChat);
                db.ChatMessages.AddRange(
                    NewAssistantMessage(goodChat, "A useful answer with preserved provenance."),
                    NewAssistantMessage(excludedChat, "A temporary test answer."));
                db.DeepResearchRuns.Add(new DeepResearchRun
                {
                    CompanyId = approved.Id,
                    Question = "stale queued work",
                    Model = "fixture-model",
                    Status = DeepResearchRunStatus.Queued
                });
                db.BriefingGenerationJobs.Add(new BriefingGenerationJob
                {
                    CompanyId = approved.Id,
                    Operation = BriefingGenerationOperation.Create,
                    RequestJson = "{}",
                    Status = BriefingGenerationStatus.Queued
                });
                db.ExternalResearchAnalysisJobs.Add(new ExternalResearchAnalysisJob
                {
                    CompanyId = approved.Id,
                    Question = "temporary analysis",
                    RawResponse = "fixture only",
                    Status = ExternalResearchAnalysisStatus.Queued
                });
                db.ProviderCredentials.Add(new ProviderCredentialEntity
                {
                    Provider = "brave",
                    Ciphertext = [1, 2, 3],
                    Nonce = [4, 5, 6],
                    AuthenticationTag = [7, 8, 9],
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                db.Users.Add(new ApplicationUser
                {
                    UserName = "developer@example.invalid",
                    Email = "developer@example.invalid",
                    DisplayName = "Fixture developer",
                    EmailConfirmed = true
                });
                await db.SaveChangesAsync();
            }

            var before = SHA256.HashData(await File.ReadAllBytesAsync(source));
            var manifest = new DemoSeedManifest([
                new DemoCompanySelection(approved.Id, "showcase", [excludedConversationId])
            ]);
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            await DemoDatabaseCurator.ExportAsync(new DemoSeedExportOptions(source, manifestPath, output));

            var after = SHA256.HashData(await File.ReadAllBytesAsync(source));
            Assert.Equal(before, after);
            await using var curated = DemoDatabaseCurator.CreateContext(output);
            Assert.False(curated.Database.HasPendingModelChanges());
            Assert.Empty(await curated.Database.GetPendingMigrationsAsync());
            Assert.Equal(new[] { approved.Id }, await curated.Companies.AsNoTracking().Select(item => item.Id).ToArrayAsync());
            Assert.Equal(1, await curated.ResearchRuns.CountAsync());
            Assert.Equal(ResearchRunStatus.Cancelled, await curated.ResearchRuns.Select(item => item.Status).SingleAsync());
            Assert.Equal(1, await curated.SourceDocuments.CountAsync());
            var accepted = await new CompanyProfilePersistenceService(curated).GetCurrentProfileAsync(approved.Id);
            Assert.NotNull(accepted);
            Assert.Equal("A source-backed accepted profile.", accepted!.Summary);
            var evidence = Assert.Single(accepted.Evidence);
            Assert.Contains(sourceDocumentId, evidence.SourceDocumentIds);
            Assert.Equal(new[] { goodConversationId }, await curated.ChatConversations.Select(item => item.Id).ToArrayAsync());
            Assert.Equal(0, await curated.DeepResearchRuns.CountAsync());
            Assert.Equal(0, await curated.BriefingGenerationJobs.CountAsync());
            Assert.Equal(0, await curated.ExternalResearchAnalysisJobs.CountAsync());
            Assert.Equal(0, await curated.ProviderCredentials.CountAsync());
            Assert.Equal(0, await curated.Users.CountAsync());
            Assert.Equal(1, await curated.ResearchSettings.CountAsync());

            await using var sqlite = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = output,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            await sqlite.OpenAsync();
            await using var integrity = sqlite.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check";
            Assert.Equal("ok", await integrity.ExecuteScalarAsync());
            await using var foreignKeys = sqlite.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_key_check";
            await using var violations = await foreignKeys.ExecuteReaderAsync();
            Assert.False(await violations.ReadAsync());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static Company NewCompany(string name) => new() { Name = name, Country = "Vietnam" };

    private static ResearchRun NewResearchRun(Company company, ResearchRunStatus status) => new()
    {
        CompanyId = company.Id,
        Company = company,
        Status = status,
        RequestedSearchProvider = "fixture-search",
        RequestedCrawlerProvider = "fixture-crawler"
    };

    private static SourceDocument NewSource(Company company, ResearchRun run, Guid id) => new()
    {
        Id = id,
        CompanyId = company.Id,
        Company = company,
        ResearchRunId = run.Id,
        ResearchRun = run,
        Url = "https://approved.example/about",
        NormalizedUrl = "https://approved.example/about",
        SourceDomain = "approved.example",
        SourceKind = SourceKind.OfficialWebsite,
        RetrievedAt = DateTimeOffset.UtcNow,
        Content = "Fixture source evidence.",
        ContentHash = Guid.NewGuid().ToString("N"),
        CrawlerProvider = "fixture-crawler"
    };

    private static ChatConversation NewConversation(Company company, CompanyProfileVersion profile, Guid id) => new()
    {
        Id = id,
        CompanyId = company.Id,
        Company = company,
        ProfileVersionId = profile.Id,
        ProfileVersion = profile
    };

    private static ChatMessage NewAssistantMessage(ChatConversation conversation, string content) => new()
    {
        ConversationId = conversation.Id,
        Conversation = conversation,
        Role = ChatMessageRole.Assistant,
        Content = content,
        Status = ChatMessageStatus.Completed,
        AnswerStatus = ChatAnswerStatus.Answered
    };
}
