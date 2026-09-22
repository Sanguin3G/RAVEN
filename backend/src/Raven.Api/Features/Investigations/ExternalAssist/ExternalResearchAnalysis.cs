using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Research.ExternalImport;

public enum ExternalResearchAnalysisStatus
{
    Queued,
    Analyzing,
    Completed,
    Failed
}

/// <summary>
/// Durable analysis state for one pasted response. The raw response is kept on
/// the job until the user saves or discards the reviewable result.
/// </summary>
public sealed class ExternalResearchAnalysisJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public required string Question { get; init; }
    public required string RawResponse { get; init; }
    public ExternalResearchAnalysisStatus Status { get; set; } = ExternalResearchAnalysisStatus.Queued;
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed record StartExternalResearchAnalysisRequest(string Question, string Markdown);

public sealed record ExternalResearchAnalysisResponse(
    Guid Id,
    Guid CompanyId,
    string Question,
    ExternalResearchAnalysisStatus Status,
    ExternalResearchImportResult? Result,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? Error);

public interface IExternalResearchAnalysisJobStore
{
    Task AddAsync(ExternalResearchAnalysisJob job, CancellationToken cancellationToken = default);
    Task<ExternalResearchAnalysisJob?> GetAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken = default);
    Task UpdateAsync(ExternalResearchAnalysisJob job, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExternalResearchAnalysisJob>> ListActiveAsync(CancellationToken cancellationToken = default);
}

public interface IExternalResearchAnalysisQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken = default);
}

public interface IExternalResearchAnalysisService
{
    Task<ExternalResearchAnalysisResponse> StartAsync(Guid companyId, StartExternalResearchAnalysisRequest request, CancellationToken cancellationToken = default);
    Task<ExternalResearchAnalysisResponse?> GetAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken = default);
    Task<ExternalResearchAnalysisResponse?> ProcessAsync(Guid jobId, CancellationToken cancellationToken = default);
}

public sealed class EfExternalResearchAnalysisJobStore(RavenDbContext db) : IExternalResearchAnalysisJobStore
{
    public async Task AddAsync(ExternalResearchAnalysisJob job, CancellationToken cancellationToken = default)
    {
        db.ExternalResearchAnalysisJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<ExternalResearchAnalysisJob?> GetAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken = default) =>
        db.ExternalResearchAnalysisJobs.SingleOrDefaultAsync(job => job.CompanyId == companyId && job.Id == jobId, cancellationToken);

    public async Task UpdateAsync(ExternalResearchAnalysisJob job, CancellationToken cancellationToken = default)
    {
        db.ExternalResearchAnalysisJobs.Update(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalResearchAnalysisJob>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        (await db.ExternalResearchAnalysisJobs
            .Where(job => job.Status == ExternalResearchAnalysisStatus.Queued || job.Status == ExternalResearchAnalysisStatus.Analyzing)
            .ToListAsync(cancellationToken))
        .OrderBy(job => job.CreatedAt)
        .ToArray();
}

public sealed class ExternalResearchAnalysisQueue : IExternalResearchAnalysisQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default) => channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken = default) => channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class ExternalResearchAnalysisService(
    IExternalResearchAnalysisJobStore jobs,
    IExternalResearchAnalysisQueue queue,
    IExternalResearchImportParser parser,
    RavenDbContext db) : IExternalResearchAnalysisService
{
    private const int MaxMarkdownLength = 200_000;
    private const int MaxQuestionLength = 4_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ExternalResearchAnalysisResponse> StartAsync(Guid companyId, StartExternalResearchAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (companyId == Guid.Empty || !await db.Companies.AsNoTracking().AnyAsync(company => company.Id == companyId, cancellationToken))
        {
            throw new KeyNotFoundException("The requested company was not found.");
        }

        var question = request.Question?.Trim();
        var markdown = request.Markdown?.Trim();
        if (string.IsNullOrWhiteSpace(question) || question.Length > MaxQuestionLength || string.IsNullOrWhiteSpace(markdown) || markdown.Length > MaxMarkdownLength)
        {
            throw new ArgumentException("A question and pasted response within the supported limits are required.", nameof(request));
        }

        var job = new ExternalResearchAnalysisJob { CompanyId = companyId, Question = question, RawResponse = markdown };
        await jobs.AddAsync(job, cancellationToken);
        await queue.EnqueueAsync(job.Id, cancellationToken);
        return ToResponse(job);
    }

    public async Task<ExternalResearchAnalysisResponse?> GetAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || jobId == Guid.Empty) return null;
        var job = await jobs.GetAsync(companyId, jobId, cancellationToken);
        return job is null ? null : ToResponse(job);
    }

    public async Task<ExternalResearchAnalysisResponse?> ProcessAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await db.ExternalResearchAnalysisJobs.SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null || job.Status is ExternalResearchAnalysisStatus.Completed or ExternalResearchAnalysisStatus.Failed) return job is null ? null : ToResponse(job);

        job.Status = ExternalResearchAnalysisStatus.Analyzing;
        await jobs.UpdateAsync(job, cancellationToken);
        try
        {
            var result = parser.Parse(job.RawResponse);
            job.ResultJson = JsonSerializer.Serialize(result, JsonOptions);
            job.Status = ExternalResearchAnalysisStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Error = null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            job.Status = ExternalResearchAnalysisStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Error = exception.Message[..Math.Min(exception.Message.Length, 4_000)];
        }

        await jobs.UpdateAsync(job, cancellationToken);
        return ToResponse(job);
    }

    private static ExternalResearchAnalysisResponse ToResponse(ExternalResearchAnalysisJob job)
    {
        ExternalResearchImportResult? result = null;
        if (!string.IsNullOrWhiteSpace(job.ResultJson))
        {
            try { result = JsonSerializer.Deserialize<ExternalResearchImportResult>(job.ResultJson, JsonOptions); }
            catch (JsonException) { }
        }

        return new ExternalResearchAnalysisResponse(job.Id, job.CompanyId, job.Question, job.Status, result, job.CreatedAt, job.CompletedAt, job.Error);
    }
}

public sealed class ExternalResearchAnalysisWorker(
    IExternalResearchAnalysisQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ExternalResearchAnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueActiveJobsAsync(stoppingToken);
        await foreach (var jobId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IExternalResearchAnalysisService>();
                await service.ProcessAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "External research analysis worker failed for job {JobId}", jobId);
            }
        }
    }

    private async Task RequeueActiveJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IExternalResearchAnalysisJobStore>();
            foreach (var job in await store.ListActiveAsync(cancellationToken)) await queue.EnqueueAsync(job.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "External research analysis worker could not restore active jobs");
        }
    }
}
