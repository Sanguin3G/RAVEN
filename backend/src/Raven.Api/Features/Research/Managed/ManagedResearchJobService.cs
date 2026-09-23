using System.Text.Json;
using Raven.Api.Features.Settings;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>Clock seam for deterministic managed research tests.</summary>
public interface IManagedResearchClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemManagedResearchClock : IManagedResearchClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Owns managed-job transitions and the provider-to-investigation handoff. It
/// has no profile writer and therefore cannot silently mutate accepted truth.
/// </summary>
public sealed class ManagedResearchJobService(
    IManagedResearchJobStore jobs,
    IManagedResearchCompanyContextReader contextReader,
    IManagedResearchAgentClient client,
    IManagedResearchInvestigationStore investigations,
    IManagedResearchClock clock,
    IManagedResearchJobQueue queue,
    IResearchSettingsService? settings = null,
    ManagedResearchChatBridge? chatBridge = null) : IManagedResearchJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ManagedResearchJobResponse> StartAsync(
        Guid companyId,
        StartManagedResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCompanyId(companyId);
        ValidateOptionalId(request.ConversationId, nameof(request.ConversationId));
        ValidateOptionalId(request.ChatMessageId, nameof(request.ChatMessageId));
        if (!Enum.IsDefined(request.Effort))
        {
            throw new ArgumentException("The managed research effort is not supported.", nameof(request));
        }

        var objective = ManagedResearchQuestionValidation.NormalizeRequired(request.Objective, nameof(request.Objective));

        var context = await contextReader.GetAsync(companyId, cancellationToken);
        if (context is null)
        {
            throw new KeyNotFoundException("The requested company was not found.");
        }

        if (context.CompanyId != companyId)
        {
            throw new InvalidOperationException("The company context does not match the requested company.");
        }

        if (!string.IsNullOrWhiteSpace(request.ContextRevision) &&
            !ManagedResearchBriefContext.MatchesContextRevision(context, request.ContextRevision))
        {
            throw new ManagedResearchPreviewStaleException();
        }

        var configuredDepth = settings is null
            ? ManagedResearchDepth.Adaptive
            : (await settings.GetAsync(cancellationToken)).ManagedResearchDepth;

        if (request.AnswerInChat && (request.ConversationId is null || request.Purpose != ManagedResearchPurpose.General || chatBridge is null))
            throw new ArgumentException("Chat Deep Research requires an accepted-profile conversation.", nameof(request));
        var chatMessageId = request.AnswerInChat
            ? await chatBridge!.PrepareAsync(companyId, request.ConversationId!.Value, objective, cancellationToken)
            : request.ChatMessageId;

        var job = new ManagedResearchJob
        {
            CompanyId = companyId,
            ConversationId = request.ConversationId,
            ChatMessageId = chatMessageId,
            AnswerInChat = request.AnswerInChat,
            Objective = objective,
            ProviderQuery = ManagedResearchQueryBuilder.Build(context, objective),
            Effort = ToEffortValue(request.Effort == ManagedResearchEffort.Auto ? ManagedResearchEffortResolver.FromDepth(configuredDepth) : request.Effort),
            Purpose = request.Purpose,
            CreatedAt = clock.UtcNow,
            Provider = ExaAgentClient.ProviderId
        };

        await jobs.AddAsync(job, cancellationToken);
        await queue.EnqueueAsync(job.Id, cancellationToken);
        return ToResponse(job);
    }

    public async Task<ManagedResearchJobResponse?> GetAsync(
        Guid companyId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateCompanyId(companyId);
        ValidateJobId(jobId);
        var job = await jobs.GetAsync(companyId, jobId, cancellationToken);
        return job is null ? null : ToResponse(job);
    }

    public async Task<IReadOnlyList<ManagedResearchJobResponse>> ListForCompanyAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        ValidateCompanyId(companyId);
        return (await jobs.ListForCompanyAsync(companyId, cancellationToken)).Select(ToResponse).ToArray();
    }

    public async Task<ManagedResearchJobResponse?> CancelAsync(
        Guid companyId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateCompanyId(companyId);
        ValidateJobId(jobId);
        var job = await jobs.GetAsync(companyId, jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        if (job.IsTerminal)
        {
            return ToResponse(job);
        }

        job.RequestCancellation(clock.UtcNow);
        if (string.IsNullOrWhiteSpace(job.ProviderRunId))
        {
            job.Cancel(clock.UtcNow);
        }
        else
        {
            try
            {
                var providerRun = await client.CancelAsync(job.ProviderRunId, cancellationToken);
                job.ProviderRunStatus = providerRun.Status;
                if (providerRun.Status == ManagedResearchProviderRunStatus.Cancelled)
                {
                    job.Cancel(clock.UtcNow);
                }
            }
            catch (ManagedResearchProviderException)
            {
                // Keep the durable cancellation request so the worker retries it;
                // do not claim that provider work was cancelled when it was not.
            }
        }

        await jobs.UpdateAsync(job, cancellationToken);
        return ToResponse(job);
    }

    /// <summary>
    /// Performs one durable poll step. The worker requeues non-terminal jobs,
    /// so a process restart can resume from the provider run ID.
    /// </summary>
    public async Task<ManagedResearchJobResponse?> ProcessAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateJobId(jobId);
        var job = await jobs.GetAsync(jobId, cancellationToken);
        if (job is null || job.IsTerminal)
        {
            return job is null ? null : ToResponse(job);
        }

        try
        {
            if (job.CancelRequestedAt is not null)
            {
                if (!string.IsNullOrWhiteSpace(job.ProviderRunId))
                {
                    var cancelled = await client.CancelAsync(job.ProviderRunId, cancellationToken);
                    job.ProviderRunStatus = cancelled.Status;
                    if (cancelled.Status is ManagedResearchProviderRunStatus.Cancelled or ManagedResearchProviderRunStatus.Failed)
                    {
                        job.Cancel(clock.UtcNow);
                    }
                }
                else
                {
                    job.Cancel(clock.UtcNow);
                }

                await jobs.UpdateAsync(job, cancellationToken);
                return ToResponse(job);
            }

            if (string.IsNullOrWhiteSpace(job.ProviderRunId))
            {
                var created = await client.CreateAsync(
                    new ManagedResearchAgentCreateRequest(job.ProviderQuery, ParseEffort(job.Effort)),
                    cancellationToken);
                job.Provider = created.Provider;
                job.MarkResearching(created.Id, clock.UtcNow, created.Status);
                job.ProviderCostDollars = created.CostDollars;
                await jobs.UpdateAsync(job, cancellationToken);
            }

            var providerRun = await client.GetAsync(job.ProviderRunId!, cancellationToken);
            job.Provider = providerRun.Provider;
            job.ProviderRunStatus = providerRun.Status;
            job.ProviderCostDollars = providerRun.CostDollars;

            switch (providerRun.Status)
            {
                case ManagedResearchProviderRunStatus.Queued:
                case ManagedResearchProviderRunStatus.Running:
                    job.MarkResearching(providerRun.Id, job.StartedAt ?? clock.UtcNow, providerRun.Status);
                    await jobs.UpdateAsync(job, cancellationToken);
                    break;

                case ManagedResearchProviderRunStatus.Cancelled:
                    job.Cancel(clock.UtcNow);
                    await jobs.UpdateAsync(job, cancellationToken);
                    break;

                case ManagedResearchProviderRunStatus.Failed:
                    job.Fail(providerRun.Error ?? "Managed AI Research failed at the provider.", clock.UtcNow);
                    await jobs.UpdateAsync(job, cancellationToken);
                    break;

                case ManagedResearchProviderRunStatus.Completed:
                    await CompleteAsync(job, providerRun, cancellationToken);
                    break;
            }

            return ToResponse(job);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host/request cancellation must not turn a resumable provider job
            // into a false failure. The persisted row remains active.
            throw;
        }
        catch (ManagedResearchProviderException exception)
        {
            job.Fail(exception.Message, clock.UtcNow);
            await jobs.UpdateAsync(job, CancellationToken.None);
            return ToResponse(job);
        }
        catch (ManagedResearchNormalizationException exception)
        {
            job.Fail(exception.Message, clock.UtcNow);
            await jobs.UpdateAsync(job, CancellationToken.None);
            return ToResponse(job);
        }
        catch (Exception)
        {
            job.Fail("Managed AI Research failed.", clock.UtcNow);
            await jobs.UpdateAsync(job, CancellationToken.None);
            return ToResponse(job);
        }
    }

    private async Task CompleteAsync(
        ManagedResearchJob job,
        ManagedResearchProviderRun providerRun,
        CancellationToken cancellationToken)
    {
        var completedAt = providerRun.CompletedAt ?? clock.UtcNow;
        var result = ManagedResearchResultNormalizer.Normalize(job.Objective, providerRun, completedAt);
        var resultJson = JsonSerializer.Serialize(result, JsonOptions);
        var investigationId = job.InvestigationId ?? Guid.NewGuid();
        job.InvestigationId = investigationId;

        await investigations.SaveAsync(new ManagedResearchInvestigation
        {
            Id = investigationId,
            JobId = job.Id,
            CompanyId = job.CompanyId,
            ConversationId = job.ConversationId,
            ChatMessageId = job.ChatMessageId,
            Origin = "ManagedAi",
            Objective = job.Objective,
            Summary = result.Summary,
            ResultJson = resultJson,
            CompletedAt = completedAt
        }, cancellationToken);

        job.Complete(resultJson, investigationId, completedAt, providerRun.CostDollars);
        await jobs.UpdateAsync(job, cancellationToken);
    }

    private static ManagedResearchJobResponse ToResponse(ManagedResearchJob job) =>
        new(
            job.Id,
            job.CompanyId,
            job.ConversationId,
            job.ChatMessageId,
            job.Objective,
            job.Provider ?? ExaAgentClient.ProviderId,
            job.Status,
            job.ProviderRunId,
            job.ProviderRunStatus,
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.CancelRequestedAt,
            DeserializeResult(job.ResultJson),
            job.ProviderCostDollars,
            job.InvestigationId,
            job.Error,
            job.Purpose,
            job.AnswerInChat);

    private static ManagedResearchResult? DeserializeResult(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ManagedResearchResult>(resultJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ManagedResearchEffort ParseEffort(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "low" => ManagedResearchEffort.Low,
        "medium" => ManagedResearchEffort.Medium,
        "high" => ManagedResearchEffort.High,
        "xhigh" => ManagedResearchEffort.XHigh,
        _ => ManagedResearchEffort.Auto
    };

    private static string ToEffortValue(ManagedResearchEffort effort) => effort switch
    {
        ManagedResearchEffort.Low => "low",
        ManagedResearchEffort.Medium => "medium",
        ManagedResearchEffort.High => "high",
        ManagedResearchEffort.XHigh => "xhigh",
        _ => "auto"
    };

    private static void ValidateCompanyId(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company ID is required.", nameof(companyId));
        }
    }

    private static void ValidateJobId(Guid jobId)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A managed research job ID is required.", nameof(jobId));
        }
    }

    private static void ValidateOptionalId(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} must be a non-empty ID when provided.", parameterName);
        }
    }
}
