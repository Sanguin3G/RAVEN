using System.Collections.Concurrent;
using Raven.Api.Features.Settings;

namespace Raven.Api.Features.DeepResearch;

/// <summary>
/// Owns run state transitions and cancellation. The service deliberately knows
/// only the read-only agent/tool contracts; it cannot accept or mutate a profile.
/// </summary>
public sealed class DeepResearchRunService(
    IDeepResearchRunStore store,
    IDeepResearchAgent agent,
    IDeepResearchToolset tools,
    IDeepResearchActivitySink? activity = null,
    IResearchSettingsService? settings = null) : IDeepResearchRunService
{
    private const int MaxQuestionLength = 4_000;
    private const int MaxModelLength = 200;
    private readonly ConcurrentDictionary<Guid, DeepResearchBudget> budgets = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> activeRuns = new();

    public async Task<DeepResearchRunResponse> StartAsync(
        Guid companyId,
        StartDeepResearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company ID is required.", nameof(companyId));
        }

        var question = request.Question?.Trim();
        if (string.IsNullOrWhiteSpace(question) || question.Length > MaxQuestionLength)
        {
            throw new ArgumentException($"A research question between 1 and {MaxQuestionLength} characters is required.", nameof(request));
        }

        var model = await ResolveModelAsync(request.Model, cancellationToken);
        var budget = (request.Budget ?? DeepResearchBudget.Default).Normalize();
        var run = new DeepResearchRun
        {
            CompanyId = companyId,
            ConversationId = request.ConversationId,
            Question = question,
            Model = model
        };

        await store.AddAsync(run, cancellationToken);
        budgets[run.Id] = budget;
        return ToResponse(run);
    }

    /// <summary>Executes an already queued run. API/background wiring can call this after StartAsync.</summary>
    public async Task<DeepResearchRunResponse?> ExecuteAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await store.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        if (run.IsTerminal)
        {
            return ToResponse(run);
        }

        var budget = budgets.TryGetValue(run.Id, out var configuredBudget)
            ? configuredBudget
            : DeepResearchBudget.Default;
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runCancellation.CancelAfter(budget.EffectiveMaxDuration);
        if (!activeRuns.TryAdd(run.Id, runCancellation))
        {
            return ToResponse(run);
        }

        try
        {
            if (run.CancelRequestedAt is not null)
            {
                run.Cancel();
                await store.UpdateAsync(run, cancellationToken);
                await PublishAsync(run.Id, DeepResearchActivity.RunCancelled(), cancellationToken);
                return ToResponse(run);
            }

            run.Start();
            await store.UpdateAsync(run, cancellationToken);
            await PublishAsync(run.Id, DeepResearchActivity.RunStarted(), cancellationToken);

            var request = new DeepResearchAgentRequest(
                run.Id,
                run.CompanyId,
                run.Question,
                run.Model,
                budget,
                tools,
                activity);
            var result = await agent.RunAsync(request, runCancellation.Token);
            run.ToolCalls = Math.Clamp(result.Usage.ToolCalls, 0, budget.MaxToolCalls);
            run.SearchCalls = Math.Clamp(result.Usage.SearchCalls, 0, budget.MaxSearchCalls);
            run.CrawlCalls = Math.Clamp(result.Usage.CrawlCalls, 0, budget.MaxCrawlCalls);
            run.DocumentsRead = Math.Clamp(result.Usage.DocumentsRead, 0, budget.MaxDocuments);

            if (run.CancelRequestedAt is not null || result.Cancelled)
            {
                run.Cancel();
                await store.UpdateAsync(run, CancellationToken.None);
                await PublishAsync(run.Id, DeepResearchActivity.RunCancelled(), CancellationToken.None);
            }
            else if (!string.IsNullOrWhiteSpace(result.Error))
            {
                run.Fail(result.Error);
                await store.UpdateAsync(run, CancellationToken.None);
                await PublishAsync(run.Id, DeepResearchActivity.RunFailed(result.Error), CancellationToken.None);
            }
            else
            {
                run.Complete(BoundResult(result.ResultMarkdown));
                await store.UpdateAsync(run, CancellationToken.None);
                await PublishAsync(run.Id, DeepResearchActivity.RunCompleted(), CancellationToken.None);
            }

            return ToResponse(run);
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            run.ToolCalls = Math.Clamp(run.ToolCalls, 0, budget.MaxToolCalls);
            if (run.CancelRequestedAt is not null || cancellationToken.IsCancellationRequested)
            {
                run.Cancel();
            }
            else
            {
                run.Fail("The Deep Research duration budget was reached.");
            }

            await store.UpdateAsync(run, CancellationToken.None);
            await PublishAsync(
                run.Id,
                run.Status == DeepResearchRunStatus.Cancelled
                    ? DeepResearchActivity.RunCancelled()
                    : DeepResearchActivity.RunFailed(run.Error),
                CancellationToken.None);
            return ToResponse(run);
        }
        catch (Exception)
        {
            run.Fail("The Deep Research execution failed.");
            await store.UpdateAsync(run, CancellationToken.None);
            await PublishAsync(run.Id, DeepResearchActivity.RunFailed(run.Error), CancellationToken.None);
            return ToResponse(run);
        }
        finally
        {
            activeRuns.TryRemove(run.Id, out _);
        }
    }

    public async Task<DeepResearchRunResponse?> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await store.GetAsync(runId, cancellationToken);
        return run is null ? null : ToResponse(run);
    }

    public async Task<IReadOnlyList<DeepResearchRunResponse>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        (await store.ListForCompanyAsync(companyId, cancellationToken)).Select(ToResponse).ToArray();

    public async Task<DeepResearchRunResponse?> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await store.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        run.RequestCancellation();
        await store.UpdateAsync(run, cancellationToken);
        if (activeRuns.TryGetValue(run.Id, out var active))
        {
            active.Cancel();
        }

        return ToResponse(run);
    }

    private async Task<string> ResolveModelAsync(string? requestedModel, CancellationToken cancellationToken)
    {
        var model = requestedModel?.Trim();
        if (string.IsNullOrWhiteSpace(model) && settings is not null)
        {
            model = (await settings.GetAsync(cancellationToken)).DeepResearchModel;
        }

        model = string.IsNullOrWhiteSpace(model) ? "gemini-3.8-flash" : model;
        if (model.Length > MaxModelLength)
        {
            throw new ArgumentException($"The model identifier cannot exceed {MaxModelLength} characters.", nameof(requestedModel));
        }

        return model;
    }

    private Task PublishAsync(Guid runId, DeepResearchActivityEvent eventData, CancellationToken cancellationToken) =>
        activity is null ? Task.CompletedTask : activity.PublishAsync(runId, eventData, cancellationToken);

    private static string BoundResult(string? markdown)
    {
        var result = markdown?.Trim() ?? string.Empty;
        return result[..Math.Min(40_000, result.Length)];
    }

    private static DeepResearchRunResponse ToResponse(DeepResearchRun run) =>
        new(
            run.Id,
            run.CompanyId,
            run.ConversationId,
            run.Question,
            run.Status,
            run.Model,
            run.CreatedAt,
            run.StartedAt,
            run.CompletedAt,
            run.CancelRequestedAt,
            run.ToolCalls,
            run.SearchCalls,
            run.CrawlCalls,
            run.DocumentsRead,
            run.ResultMarkdown,
            run.Error);
}

public interface IDeepResearchRunService
{
    Task<DeepResearchRunResponse> StartAsync(Guid companyId, StartDeepResearchRequest request, CancellationToken cancellationToken = default);
    Task<DeepResearchRunResponse?> ExecuteAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<DeepResearchRunResponse?> GetAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeepResearchRunResponse>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<DeepResearchRunResponse?> CancelAsync(Guid runId, CancellationToken cancellationToken = default);
}
