using Raven.Api.Features.Profiles;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;

namespace Raven.Api.Features.Monitoring;

/// <summary>
/// Small in-process scheduler. It runs only while the API is alive and turns
/// due monitoring preferences into review-ready profile candidates; it never
/// confirms a profile on behalf of a user.
/// </summary>
public sealed class CompanyMonitoringWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CompanyMonitoringWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(15);
    private const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Company Monitoring poll failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ICompanyMonitoringStore>();
        var clock = scope.ServiceProvider.GetRequiredService<IMonitoringClock>();
        var due = await store.ListDueAsync(clock.UtcNow, BatchSize, cancellationToken);

        foreach (var candidate in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claimId = Guid.NewGuid();
            var claimed = await store.TryClaimDueAsync(
                candidate.CompanyId,
                claimId,
                clock.UtcNow,
                ClaimLease,
                cancellationToken);
            if (claimed is null)
            {
                continue;
            }

            await RunClaimedResearchAsync(scope.ServiceProvider, claimed, claimId, cancellationToken);
        }
    }

    private async Task RunClaimedResearchAsync(
        IServiceProvider services,
        CompanyMonitoringSetting setting,
        Guid claimId,
        CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<ICompanyMonitoringStore>();
        var monitoring = services.GetRequiredService<ICompanyMonitoringService>();
        var clock = services.GetRequiredService<IMonitoringClock>();
        var research = services.GetRequiredService<IResearchCompanyService>();
        var profiles = services.GetRequiredService<ICompanyProfileWorkflowService>();
        var events = services.GetRequiredService<IResearchEventWriter>();

        try
        {
            var run = await research.DiscoverAsync(
                setting.CompanyId,
                new DiscoverResearchRequest(
                    ResearchHint: "Scheduled monitoring refresh",
                    GroundingMode: GroundingMode.Always,
                    UseAcceptedProfileIdentity: true),
                cancellationToken);
            if (run is null || run.Stage == ResearchStage.Failed)
            {
                throw new InvalidOperationException(run?.Error ?? "Monitoring could not start research.");
            }

            await events.WriteAsync(new ResearchEvent
            {
                ResearchRunId = run.Id,
                Stage = run.Stage,
                Category = ResearchEventCategory.MonitoringRunStarted,
                Status = ResearchEventStatus.Working,
                OutputSummary = "Scheduled monitoring started a review-required research refresh."
            }, cancellationToken);

            if (run.Stage == ResearchStage.AwaitingIdentitySelection)
            {
                var identities = await research.ListIdentityCandidatesAsync(run.Id, cancellationToken) ?? [];
                var selected = identities.FirstOrDefault(identity => identity.Recommended) ?? identities.FirstOrDefault();
                if (selected is null)
                {
                    throw new InvalidOperationException("Monitoring could not resolve a research target.");
                }

                run = await research.SelectIdentityAsync(run.Id, new SelectResearchIdentityRequest(selected.Id), cancellationToken)
                    ?? throw new InvalidOperationException("Monitoring could not select its grounded research target.");
            }

            if (run.Stage != ResearchStage.AwaitingSourceSelection)
            {
                throw new InvalidOperationException(run.Error ?? "Monitoring did not reach source selection.");
            }

            var candidates = await research.ListCandidatesAsync(run.Id, cancellationToken) ?? [];
            var recommendedIds = candidates.Where(candidate => candidate.Recommended).Select(candidate => candidate.Id).ToArray();
            if (recommendedIds.Length == 0)
            {
                throw new InvalidOperationException("Monitoring found no safe recommended sources to acquire.");
            }

            run = await research.AcquireAsync(run.Id, new AcquireResearchCandidatesRequest(recommendedIds), cancellationToken)
                ?? throw new InvalidOperationException("Monitoring could not acquire recommended evidence.");
            if (run.Stage != ResearchStage.EvidenceReady)
            {
                throw new InvalidOperationException(run.Error ?? "Monitoring evidence acquisition failed.");
            }

            var generated = await profiles.GenerateAsync(run.Id, cancellationToken);
            if (generated is null || !generated.Succeeded || generated.Candidate is null)
            {
                throw new InvalidOperationException(generated?.Failure?.Message ?? "Monitoring profile generation failed.");
            }

            monitoring.MarkUpdateReadyForReview(setting, claimId, clock.UtcNow);
            await store.SaveAsync(setting, cancellationToken);
            await events.WriteAsync(new ResearchEvent
            {
                ResearchRunId = run.Id,
                Stage = ResearchStage.AwaitingProfileConfirmation,
                Category = ResearchEventCategory.MonitoringUpdateReady,
                Status = ResearchEventStatus.WaitingForUser,
                OutputSummary = "Scheduled research produced a profile candidate that requires human review."
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            monitoring.MarkCancelled(setting, claimId, clock.UtcNow);
            await store.SaveAsync(setting, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Scheduled monitoring failed for company {CompanyId}.", setting.CompanyId);
            monitoring.MarkFailed(setting, claimId, clock.UtcNow);
            await store.SaveAsync(setting, CancellationToken.None);
        }
    }
}
