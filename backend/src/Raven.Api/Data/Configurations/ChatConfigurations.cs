using Microsoft.EntityFrameworkCore;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Companies.Workspace;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Identity;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Settings;
using Raven.Api.Features.Profiles.Changes;
using Raven.Api.Features.Monitoring;
using Raven.Api.Features.DeepResearch;
using Raven.Api.Features.Research.SavedArtifacts;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.Organization;
using Raven.Api.Features.Research.ExternalImport;
using Raven.Api.Features.Chat;
using Raven.Api.Features.ManagedResearch;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raven.Api.Data.Configurations;

public sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> entity)
    {
            entity.HasKey(conversation => conversation.Id);
            entity.Property(conversation => conversation.Title).HasMaxLength(200);
            entity.Property(conversation => conversation.WebSearchEnabled).HasDefaultValue(false);
            entity.HasIndex(conversation => new { conversation.CompanyId, conversation.UpdatedAt });
            entity.HasOne(conversation => conversation.Company)
                .WithMany()
                .HasForeignKey(conversation => conversation.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(conversation => conversation.ProfileVersion)
                .WithMany()
                .HasForeignKey(conversation => conversation.ProfileVersionId)
                .OnDelete(DeleteBehavior.Restrict);

    }
}

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> entity)
    {
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(message => message.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(message => message.WebLookupIncomplete).HasColumnName("WebLookupIncomplete");
            entity.Property(message => message.AnswerStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(message => message.Content).HasMaxLength(20_000).IsRequired();
            entity.Property(message => message.FollowUpQuestion).HasMaxLength(1_000);
            entity.Property(message => message.AiProvider).HasMaxLength(100);
            entity.Property(message => message.AiModel).HasMaxLength(200);
            entity.Property(message => message.Activity).HasMaxLength(100);
            entity.HasIndex(message => new { message.ConversationId, message.CreatedAt });
            entity.HasIndex(message => message.ManagedResearchJobId).IsUnique();
            entity.HasOne(message => message.Conversation)
                .WithMany(conversation => conversation.Messages)
                .HasForeignKey(message => message.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

    }
}

public sealed class ChatWebEvidenceSnapshotConfiguration : IEntityTypeConfiguration<ChatWebEvidenceSnapshot>
{
    public void Configure(EntityTypeBuilder<ChatWebEvidenceSnapshot> entity)
    {
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.Url).HasMaxLength(2_000).IsRequired();
            entity.Property(snapshot => snapshot.NormalizedUrl).HasMaxLength(2_000).IsRequired();
            entity.Property(snapshot => snapshot.Title).HasMaxLength(500);
            entity.Property(snapshot => snapshot.SearchSnippet).HasMaxLength(2_000);
            entity.Property(snapshot => snapshot.ContentExcerpt).HasMaxLength(8_000).IsRequired();
            entity.Property(snapshot => snapshot.SearchProvider).HasMaxLength(100).IsRequired();
            entity.Property(snapshot => snapshot.CrawlerProvider).HasMaxLength(100);
            entity.HasIndex(snapshot => new { snapshot.ChatMessageId, snapshot.NormalizedUrl }).IsUnique();
            entity.HasOne(snapshot => snapshot.ChatMessage)
                .WithMany(message => message.WebEvidenceSnapshots)
                .HasForeignKey(snapshot => snapshot.ChatMessageId)
                .OnDelete(DeleteBehavior.Cascade);

    }
}

public sealed class ChatCitationConfiguration : IEntityTypeConfiguration<ChatCitation>
{
    public void Configure(EntityTypeBuilder<ChatCitation> entity)
    {
            entity.HasKey(citation => citation.Id);
            entity.Property(citation => citation.Origin).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(citation => citation.FieldPath).HasMaxLength(300);
            entity.Property(citation => citation.Excerpt).HasMaxLength(1_000);
            entity.HasIndex(citation => new { citation.ChatMessageId, citation.SourceDocumentId }).IsUnique();
            entity.HasIndex(citation => new { citation.ChatMessageId, citation.WebEvidenceSnapshotId }).IsUnique();
            entity.HasIndex(citation => new { citation.ChatMessageId, citation.InvestigationId }).IsUnique();
            entity.HasIndex(citation => new { citation.ChatMessageId, citation.SavedResearchArtifactId }).IsUnique();
            entity.HasIndex(citation => new { citation.ChatMessageId, citation.BriefingVersionId }).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_ChatCitations_ExactlyOneEvidence",
                "(\"SourceDocumentId\" IS NOT NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NOT NULL AND \"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NOT NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NOT NULL AND \"BriefingVersionId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingVersionId\" IS NOT NULL)"));
            entity.HasOne(citation => citation.ChatMessage)
                .WithMany(message => message.Citations)
                .HasForeignKey(citation => citation.ChatMessageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(citation => citation.SourceDocument)
                .WithMany()
                .HasForeignKey(citation => citation.SourceDocumentId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(citation => citation.WebEvidenceSnapshot)
                .WithMany()
                .HasForeignKey(citation => citation.WebEvidenceSnapshotId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(citation => citation.Investigation)
                .WithMany()
                .HasForeignKey(citation => citation.InvestigationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(citation => citation.SavedResearchArtifact)
                .WithMany()
                .HasForeignKey(citation => citation.SavedResearchArtifactId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(citation => citation.BriefingVersion)
                .WithMany()
                .HasForeignKey(citation => citation.BriefingVersionId)
                .OnDelete(DeleteBehavior.Restrict);

    }
}

public sealed class ChatToolExecutionConfiguration : IEntityTypeConfiguration<ChatToolExecution>
{
    public void Configure(EntityTypeBuilder<ChatToolExecution> entity)
    {
            entity.HasKey(execution => execution.Id);
            entity.Property(execution => execution.Tool).HasMaxLength(200).IsRequired();
            entity.Property(execution => execution.Provider).HasMaxLength(100).IsRequired();
            entity.Property(execution => execution.Status).HasMaxLength(32).IsRequired();
            entity.Property(execution => execution.InputSummary).HasMaxLength(2_000);
            entity.Property(execution => execution.OutputSummary).HasMaxLength(2_000);
            entity.Property(execution => execution.ErrorCode).HasMaxLength(100);
            entity.HasIndex(execution => new { execution.ChatMessageId, execution.CreatedAt });
            entity.HasOne(execution => execution.ChatMessage)
                .WithMany(message => message.ToolExecutions)
                .HasForeignKey(execution => execution.ChatMessageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(execution => execution.ResearchRun)
                .WithMany()
                .HasForeignKey(execution => execution.ResearchRunId)
                .OnDelete(DeleteBehavior.Restrict);

    }
}
