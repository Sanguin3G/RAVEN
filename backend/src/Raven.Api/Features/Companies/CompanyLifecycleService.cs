using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Companies.Lifecycle;

namespace Raven.Api.Features.Companies;

/// <summary>
/// Performs user-confirmed workspace lifecycle operations. Archive/restore/delete
/// remain here; merge reconciliation is delegated to CompanyMergeService.
/// </summary>
public sealed class CompanyLifecycleService(
    RavenDbContext dbContext,
    CompanyDeletionService? deletionService = null,
    CompanyMergeService? mergeService = null) : ICompanyLifecycleService
{
    public async Task<CompanyResponse?> ArchiveAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies
            .SingleOrDefaultAsync(item => item.Id == companyId, cancellationToken);
        if (company is null)
        {
            return null;
        }

        company.ArchivedAt ??= DateTimeOffset.UtcNow;
        company.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(company);
    }

    public async Task<CompanyResponse?> RestoreAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies
            .SingleOrDefaultAsync(item => item.Id == companyId, cancellationToken);
        if (company is null)
        {
            return null;
        }

        company.ArchivedAt = null;
        company.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(company);
    }

    public async Task<CompanyDeleteResult> DeleteAsync(
        Guid companyId,
        bool confirm,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.Companies
            .AsNoTracking()
            .AnyAsync(item => item.Id == companyId, cancellationToken);
        if (!exists)
        {
            return new CompanyDeleteResult(CompanyDeleteOutcome.NotFound);
        }

        if (!confirm)
        {
            return new CompanyDeleteResult(CompanyDeleteOutcome.ConfirmationRequired);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var deletedRecords = await (deletionService ?? new CompanyDeletionService(dbContext)).DeleteAsync(companyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CompanyDeleteResult(CompanyDeleteOutcome.Deleted, deletedRecords);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task<CompanyMergePreviewResponse?> PreviewMergeAsync(
        CompanyMergePreviewRequest request,
        CancellationToken cancellationToken) =>
        (mergeService ?? new CompanyMergeService(dbContext)).PreviewAsync(request, cancellationToken);

    public Task<CompanyMergeResult> ConfirmMergeAsync(
        CompanyMergeConfirmRequest request,
        CancellationToken cancellationToken) =>
        (mergeService ?? new CompanyMergeService(dbContext)).ConfirmAsync(request, cancellationToken);

    private static CompanyResponse ToResponse(Company company) =>
        new(
            company.Id,
            company.Name,
            company.Website,
            company.Country,
            company.CreatedAt,
            company.UpdatedAt,
            company.LegalName,
            company.RegistrationNumber,
            company.Headquarters,
            company.LastResearchedAt,
            company.ArchivedAt);
}
