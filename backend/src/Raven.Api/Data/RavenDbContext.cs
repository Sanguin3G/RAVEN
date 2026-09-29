using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Raven.Api.Features.Auth;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Companies.Workspace;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Identity;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Settings;
using Raven.Api.Features.ProviderCredentials;
using Raven.Api.Features.Profiles.Changes;
using Raven.Api.Features.Monitoring;
using Raven.Api.Features.DeepResearch;
using Raven.Api.Features.Research.SavedArtifacts;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.Organization;
using Raven.Api.Features.Research.ExternalImport;
using Raven.Api.Features.Chat;
using Raven.Api.Features.ManagedResearch;
using Raven.Api.Features.Research.Briefings;

namespace Raven.Api.Data;

public sealed class RavenDbContext(DbContextOptions<RavenDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<ResearchRun> ResearchRuns => Set<ResearchRun>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<ResearchCandidate> ResearchCandidates => Set<ResearchCandidate>();
    public DbSet<ResearchEvent> ResearchEvents => Set<ResearchEvent>();
    public DbSet<CompanyProfileVersion> CompanyProfileVersions => Set<CompanyProfileVersion>();
    public DbSet<CompanyProfileCandidate> CompanyProfileCandidates => Set<CompanyProfileCandidate>();
    public DbSet<ProfileEvidence> ProfileEvidences => Set<ProfileEvidence>();
    public DbSet<ResearchSettingsEntity> ResearchSettings => Set<ResearchSettingsEntity>();
    public DbSet<ProviderCredentialEntity> ProviderCredentials => Set<ProviderCredentialEntity>();
    public DbSet<ProfileChange> ProfileChanges => Set<ProfileChange>();
    public DbSet<ResearchIdentityCandidate> ResearchIdentityCandidates => Set<ResearchIdentityCandidate>();
    public DbSet<CompanyMonitoringSetting> CompanyMonitoringSettings => Set<CompanyMonitoringSetting>();
    public DbSet<DeepResearchRun> DeepResearchRuns => Set<DeepResearchRun>();
    public DbSet<DeepResearchActivityRecord> DeepResearchActivities => Set<DeepResearchActivityRecord>();
    public DbSet<SavedResearchArtifact> SavedResearchArtifacts => Set<SavedResearchArtifact>();
    public DbSet<InvestigationOrganizationRevision> InvestigationOrganizationRevisions => Set<InvestigationOrganizationRevision>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatCitation> ChatCitations => Set<ChatCitation>();
    public DbSet<ChatWebEvidenceSnapshot> ChatWebEvidenceSnapshots => Set<ChatWebEvidenceSnapshot>();
    public DbSet<ChatToolExecution> ChatToolExecutions => Set<ChatToolExecution>();
    public DbSet<ManagedResearchJob> ManagedResearchJobs => Set<ManagedResearchJob>();
    public DbSet<ManagedResearchInvestigation> ManagedResearchInvestigations => Set<ManagedResearchInvestigation>();
    public DbSet<ResearchContextAttachment> ResearchContextAttachments => Set<ResearchContextAttachment>();
    public DbSet<ExternalResearchAnalysisJob> ExternalResearchAnalysisJobs => Set<ExternalResearchAnalysisJob>();
    public DbSet<WorkspaceResearchReviewState> WorkspaceResearchReviewStates => Set<WorkspaceResearchReviewState>();
    public DbSet<InvestigationReviewState> InvestigationReviewStates => Set<InvestigationReviewState>();
    public DbSet<ResearchBriefing> ResearchBriefings => Set<ResearchBriefing>();
    public DbSet<ResearchBriefingVersion> ResearchBriefingVersions => Set<ResearchBriefingVersion>();
    public DbSet<BriefingGenerationJob> BriefingGenerationJobs => Set<BriefingGenerationJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RavenDbContext).Assembly);
    }
}
