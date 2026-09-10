using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Profiles.Generation;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;

namespace Raven.Api.Features.Profiles;

/// <summary>Coordinates evidence-grounded generation and server-owned confirmation.</summary>
public sealed class CompanyProfileWorkflowService(
    RavenDbContext dbContext,
    IProfileGenerationService generationService,
    ICompanyProfilePersistenceService persistenceService,
    IResearchEventWriter eventWriter) : ICompanyProfileWorkflowService
{
    public async Task<ProfileGenerationResponse?> GenerateAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        var run = await dbContext.ResearchRuns
            .SingleOrDefaultAsync(item => item.Id == researchRunId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        if (run.Stage is not ResearchStage.EvidenceReady and not ResearchStage.AwaitingProfileConfirmation)
        {
            return Failed("invalid_stage", "Acquire evidence before generating a Company Profile.");
        }

        var company = await dbContext.Companies
            .SingleAsync(item => item.Id == run.CompanyId, cancellationToken);
        var sources = await dbContext.SourceDocuments
            .Where(item => item.ResearchRunId == run.Id)
            .ToListAsync(cancellationToken);
        if (sources.Count == 0)
        {
            return Failed("no_evidence", "This research run has no acquired evidence to generate from.");
        }

        run.Stage = ResearchStage.GeneratingProfile;
        run.Status = ResearchRunStatus.Searching;
        run.Error = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteEventAsync(run, ResearchEventCategory.AiRequested, ResearchEventStatus.Working,
            "Building a structured profile from acquired evidence.", cancellationToken);

        var companySourceIds = await dbContext.SourceDocuments
            .AsNoTracking()
            .Where(item => item.CompanyId == company.Id)
            .Select(item => item.Id)
            .ToHashSetAsync(cancellationToken);
        var runSourceIds = sources.Select(item => item.Id).ToHashSet();
        var result = await generationService.GenerateAsync(new ProfileGenerationInput(
            company.Id,
            run.Id,
            new ProfileIdentityHints(
                company.Name,
                company.LegalName,
                company.Website,
                company.Country,
                company.Headquarters,
                company.RegistrationNumber,
                run.ResearchHint),
            sources,
            new ProfileValidationContext(company.Id, run.Id, companySourceIds, runSourceIds)), cancellationToken);

        if (!result.Succeeded || result.Candidate is null)
        {
            run.Status = ResearchRunStatus.Failed;
            run.Stage = ResearchStage.Failed;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Error = result.Failure?.Message ?? "Profile generation did not return a valid candidate.";
            await dbContext.SaveChangesAsync(cancellationToken);
            await WriteEventAsync(run, ResearchEventCategory.AiFailed, ResearchEventStatus.Failed,
                run.Error, cancellationToken);
            return ToResponse(result);
        }

        var candidate = await persistenceService.SaveCandidateAsync(result.Candidate, cancellationToken);
        if (candidate is null)
        {
            run.Status = ResearchRunStatus.Failed;
            run.Stage = ResearchStage.Failed;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Error = "The generated profile candidate could not be validated for this evidence set.";
            await dbContext.SaveChangesAsync(cancellationToken);
            await WriteEventAsync(run, ResearchEventCategory.AiFailed, ResearchEventStatus.Failed, run.Error, cancellationToken);
            return Failed("candidate_validation_failed", run.Error, result);
        }

        run.Status = ResearchRunStatus.Completed;
        run.Stage = ResearchStage.AwaitingProfileConfirmation;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Error = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteEventAsync(run, ResearchEventCategory.AiCompleted, ResearchEventStatus.WaitingForUser,
            "Profile candidate is ready for human confirmation.", cancellationToken);
        await WriteEventAsync(run, ResearchEventCategory.ProfileValidated, ResearchEventStatus.Completed,
            $"Validated {candidate.Evidence.Count} evidence groups.", cancellationToken);

        return ToResponse(result, candidate);
    }

    public async Task<CompanyProfileVersion?> ConfirmAsync(Guid researchRunId, Guid candidateId, CancellationToken cancellationToken)
    {
        var candidate = await persistenceService.GetCandidateAsync(candidateId, cancellationToken);
        if (candidate is null || candidate.ResearchRunId != researchRunId)
        {
            return null;
        }

        var profile = await persistenceService.ConfirmCandidateAsync(candidateId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var run = await dbContext.ResearchRuns.SingleAsync(item => item.Id == researchRunId, cancellationToken);
        await WriteEventAsync(run, ResearchEventCategory.ProfileConfirmed, ResearchEventStatus.Completed,
            $"Confirmed Company Profile version {profile.Version}.", cancellationToken);
        return profile;
    }

    public Task<CompanyProfileVersion?> GetCurrentAsync(Guid companyId, CancellationToken cancellationToken) =>
        persistenceService.GetCurrentProfileAsync(companyId, cancellationToken);

    private async Task WriteEventAsync(ResearchRun run, ResearchEventCategory category, ResearchEventStatus status, string? summary, CancellationToken cancellationToken) =>
        await eventWriter.WriteAsync(new ResearchEvent
        {
            ResearchRunId = run.Id,
            Stage = run.Stage,
            Category = category,
            Status = status,
            Provider = "gemini",
            OutputSummary = summary
        }, cancellationToken);

    private static ProfileGenerationResponse Failed(string code, string message, ProfileGenerationResult? result = null) =>
        new(null, result?.Warnings ?? [message], result?.Provider ?? "gemini", result?.Model ?? "gemini-3.5-flash-lite",
            result?.PromptTemplateVersion ?? ProfileGenerationService.ProfilePromptTemplateVersion,
            (long)(result?.Duration.TotalMilliseconds ?? 0), result?.Usage,
            result?.Failure ?? new Raven.Api.Features.Ai.AiFailure(code, message, false));

    private static ProfileGenerationResponse ToResponse(ProfileGenerationResult result, CompanyProfileCandidate? candidate = null) =>
        new(candidate ?? result.Candidate, result.Warnings, result.Provider, result.Model, result.PromptTemplateVersion,
            (long)result.Duration.TotalMilliseconds, result.Usage, result.Failure);
}
