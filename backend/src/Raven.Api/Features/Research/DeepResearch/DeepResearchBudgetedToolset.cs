namespace Raven.Api.Features.DeepResearch;

/// <summary>
/// Applies per-run budgets and emits safe activity around the neutral tool
/// boundary. The wrapped provider is never called after a budget is exhausted.
/// </summary>
public sealed class DeepResearchBudgetedToolset : IDeepResearchToolset
{
    private readonly IDeepResearchToolset inner;
    private readonly DeepResearchBudget budget;
    private readonly IDeepResearchActivitySink? activity;
    private readonly Guid runId;
    private readonly object gate = new();
    private int toolCalls;
    private int searchCalls;
    private int crawlCalls;
    private int documentsRead;
    private readonly HashSet<Guid> recordedDocumentIds = [];

    public DeepResearchBudgetedToolset(
        IDeepResearchToolset inner,
        DeepResearchBudget budget,
        IDeepResearchActivitySink? activity = null,
        Guid runId = default)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.budget = budget.Normalize();
        this.activity = activity;
        this.runId = runId;
    }

    public bool SupportsStoredSourceTextSearch => inner.SupportsStoredSourceTextSearch;

    public DeepResearchUsage Usage
    {
        get
        {
            lock (gate)
            {
                return new DeepResearchUsage(toolCalls, searchCalls, crawlCalls, documentsRead);
            }
        }
    }

    public Task<DeepResearchToolResult<DeepResearchProfile>> GetCompanyProfileAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            DeepResearchToolKind.GetCompanyProfile,
            "get_company_profile",
            "tool",
            token => inner.GetCompanyProfileAsync(companyId, token),
            static result => result.SourceDocumentIds,
            cancellationToken);

    public Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>> GetCompanySourcesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            DeepResearchToolKind.GetCompanySources,
            "get_company_sources",
            "source",
            token => inner.GetCompanySourcesAsync(companyId, token),
            static result => result.Value?.Select(source => source.SourceDocumentId) ?? [],
            cancellationToken);

    public Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSearchHit>>> SearchWebAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            DeepResearchToolKind.SearchWeb,
            "search_web",
            "search",
            token => inner.SearchWebAsync(query, maxResults, token),
            static result => result.SourceDocumentIds,
            cancellationToken);

    public Task<DeepResearchToolResult<DeepResearchCrawlPage>> CrawlPageAsync(string url, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            DeepResearchToolKind.CrawlPage,
            "crawl_page",
            "crawl",
            token => inner.CrawlPageAsync(url, token),
            static result => result.SourceDocumentIds.Count > 0
                ? result.SourceDocumentIds
                : result.Value?.SourceDocumentId is { } id ? [id] : [],
            cancellationToken);

    public Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>> SearchStoredSourceTextAsync(Guid companyId, string query, int maxResults, CancellationToken cancellationToken = default)
    {
        if (!SupportsStoredSourceTextSearch)
        {
            return Task.FromResult(new DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>(
                false,
                null,
                "Stored source text search is unavailable."));
        }

        return ExecuteAsync(
            DeepResearchToolKind.SearchStoredSourceText,
            "search_stored_source_text",
            "source",
            token => inner.SearchStoredSourceTextAsync(companyId, query, maxResults, token),
            static result => result.Value?.Select(source => source.SourceDocumentId) ?? [],
            cancellationToken);
    }

    private async Task<DeepResearchToolResult<T>> ExecuteAsync<T>(
        DeepResearchToolKind kind,
        string label,
        string eventType,
        Func<CancellationToken, Task<DeepResearchToolResult<T>>> operation,
        Func<DeepResearchToolResult<T>, IEnumerable<Guid>> sourceIds,
        CancellationToken cancellationToken)
    {
        if (!TryConsume(kind, out var reason))
        {
            await PublishAsync(new DeepResearchActivityEvent(
                "budget",
                DeepResearchActivityStatus.Skipped,
                label,
                reason));
            return new DeepResearchToolResult<T>(false, default, reason);
        }

        await PublishAsync(new DeepResearchActivityEvent(eventType, DeepResearchActivityStatus.Working, label));

        try
        {
            var result = await operation(cancellationToken);
            RecordDocuments(sourceIds(result));
            await PublishAsync(new DeepResearchActivityEvent(
                eventType,
                result.Succeeded ? DeepResearchActivityStatus.Completed : DeepResearchActivityStatus.Failed,
                label,
                result.Succeeded ? BuildSuccessDetail(result, kind) : "The tool did not return usable data.",
                result.Provider,
                ReadSourceIds(result, sourceIds)));
            return LimitResultDocuments(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            await PublishAsync(new DeepResearchActivityEvent(
                eventType,
                DeepResearchActivityStatus.Failed,
                label,
                "The tool failed before returning usable data."));
            return new DeepResearchToolResult<T>(false, default, "The tool failed before returning usable data.");
        }
    }

    private bool TryConsume(DeepResearchToolKind kind, out string reason)
    {
        lock (gate)
        {
            if (toolCalls >= budget.MaxToolCalls)
            {
                reason = "The tool-call budget was reached.";
                return false;
            }

            if (kind == DeepResearchToolKind.SearchWeb && searchCalls >= budget.MaxSearchCalls)
            {
                reason = "The web-search budget was reached.";
                return false;
            }

            if (kind == DeepResearchToolKind.CrawlPage && crawlCalls >= budget.MaxCrawlCalls)
            {
                reason = "The page-crawl budget was reached.";
                return false;
            }

            toolCalls++;
            if (kind == DeepResearchToolKind.SearchWeb)
            {
                searchCalls++;
            }
            else if (kind == DeepResearchToolKind.CrawlPage)
            {
                crawlCalls++;
            }

            reason = string.Empty;
            return true;
        }
    }

    private void RecordDocuments(IEnumerable<Guid> sourceIds)
    {
        lock (gate)
        {
            var remaining = Math.Max(0, budget.MaxDocuments - documentsRead);
            foreach (var id in sourceIds.Where(id => id != Guid.Empty).Distinct().Take(remaining))
            {
                if (recordedDocumentIds.Add(id))
                {
                    documentsRead++;
                }
            }
        }
    }

    private DeepResearchToolResult<T> LimitResultDocuments<T>(DeepResearchToolResult<T> result)
    {
        Guid[] ids;
        lock (gate)
        {
            ids = result.SourceDocumentIds
                .Where(id => id != Guid.Empty && recordedDocumentIds.Contains(id))
                .Distinct()
                .Take(DeepResearchActivitySanitizer.MaxSourceDocumentIds)
                .ToArray();
        }

        var value = result.Value;
        if (value is IReadOnlyList<DeepResearchSource> sources)
        {
            var limited = sources
                .Where(source => ids.Length == 0 || ids.Contains(source.SourceDocumentId))
                .Take(budget.MaxDocuments)
                .ToArray();
            value = (T)(object)limited;
        }

        return result with { Value = value, SourceDocumentIds = ids };
    }

    private static string BuildSuccessDetail<T>(DeepResearchToolResult<T> result, DeepResearchToolKind kind) => kind switch
    {
        DeepResearchToolKind.SearchWeb when result.Value is IReadOnlyList<DeepResearchSearchHit> hits => $"Search returned {hits.Count} result(s).",
        DeepResearchToolKind.GetCompanySources when result.Value is IReadOnlyList<DeepResearchSource> sources => $"Loaded {sources.Count} stored source(s).",
        DeepResearchToolKind.SearchStoredSourceText when result.Value is IReadOnlyList<DeepResearchSource> sources => $"Stored-source search returned {sources.Count} match(es).",
        _ => "Tool completed."
    };

    private static IReadOnlyList<Guid> ReadSourceIds<T>(DeepResearchToolResult<T> result, Func<DeepResearchToolResult<T>, IEnumerable<Guid>> sourceIds) =>
        sourceIds(result).Where(id => id != Guid.Empty).Distinct().Take(DeepResearchActivitySanitizer.MaxSourceDocumentIds).ToArray();

    private Task PublishAsync(DeepResearchActivityEvent activity) =>
        this.activity is null ? Task.CompletedTask : this.activity.PublishAsync(runId, activity);
}
