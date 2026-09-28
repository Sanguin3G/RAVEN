using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Research.Briefings;

public sealed class BriefingGenerationService(RavenDbContext db, BriefingService briefings, BriefingGenerationQueue queue)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BriefingGenerationJobResponse> StartCreateAsync(Guid companyId, CreateBriefingRequest request, CancellationToken ct)
    {
        if (!await db.Companies.AsNoTracking().AnyAsync(item => item.Id == companyId, ct))
            throw new KeyNotFoundException("Company not found.");
        var job = new BriefingGenerationJob
        {
            CompanyId = companyId,
            Operation = BriefingGenerationOperation.Create,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions)
        };
        db.BriefingGenerationJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(job.Id, ct);
        return ToResponse(job);
    }

    public async Task<BriefingGenerationJobResponse?> StartUpdateAsync(Guid companyId, Guid briefingId, UpdateBriefingRequest request, CancellationToken ct)
    {
        var briefing = await db.ResearchBriefings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == briefingId, ct);
        if (briefing is null) return null;
        if (briefing.ArchivedAt is not null) throw new InvalidOperationException("Archived Briefings cannot be updated.");
        var job = new BriefingGenerationJob
        {
            CompanyId = companyId,
            BriefingId = briefingId,
            Operation = BriefingGenerationOperation.Update,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions)
        };
        db.BriefingGenerationJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(job.Id, ct);
        return ToResponse(job);
    }

    public async Task<BriefingGenerationJobResponse?> GetAsync(Guid companyId, Guid jobId, CancellationToken ct)
    {
        var job = await db.BriefingGenerationJobs.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == jobId, ct);
        return job is null ? null : ToResponse(job);
    }

    public async Task ProcessAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.BriefingGenerationJobs.SingleOrDefaultAsync(item => item.Id == jobId, ct);
        if (job is null || job.Status is BriefingGenerationStatus.Completed or BriefingGenerationStatus.Failed) return;
        job.Status = BriefingGenerationStatus.Generating;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            BriefingResponse? result;
            if (job.Operation == BriefingGenerationOperation.Create)
            {
                var request = JsonSerializer.Deserialize<CreateBriefingRequest>(job.RequestJson, JsonOptions)
                    ?? throw new InvalidOperationException("The saved create request is invalid.");
                result = await briefings.CreateAsync(job.CompanyId, request, ct);
            }
            else
            {
                if (job.BriefingId is null) throw new InvalidOperationException("The saved update request has no Briefing.");
                var request = JsonSerializer.Deserialize<UpdateBriefingRequest>(job.RequestJson, JsonOptions)
                    ?? throw new InvalidOperationException("The saved update request is invalid.");
                result = await briefings.UpdateAsync(job.CompanyId, job.BriefingId.Value, request, ct);
                if (result is null) throw new InvalidOperationException("The Briefing no longer exists.");
            }

            job.Status = BriefingGenerationStatus.Saving;
            job.ResultBriefingId = result.Id;
            job.ResultVersionNumber = result.CurrentVersion.VersionNumber;
            job.Error = null;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            job.Status = BriefingGenerationStatus.Completed;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var failed = await db.BriefingGenerationJobs.SingleAsync(item => item.Id == jobId, CancellationToken.None);
            failed.Status = BriefingGenerationStatus.Failed;
            failed.Error = "Briefing generation failed. The previous version remains unchanged.";
            failed.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static BriefingGenerationJobResponse ToResponse(BriefingGenerationJob job) => new(
        job.Id, job.CompanyId, job.BriefingId, job.Operation, job.Status, job.ResultBriefingId,
        job.ResultVersionNumber, job.Error, job.CreatedAt, job.UpdatedAt);
}
