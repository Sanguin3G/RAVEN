using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.DeepResearch;
using Raven.Api.Features.Monitoring;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Changes;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Organization;
using Raven.Api.Features.Research.SavedArtifacts;
using Raven.Api.Features.Research.Briefings;

namespace Raven.Api.Features.Companies;

public sealed partial class CompanyMergeService
{
    private async Task ReconcileMonitoringAsync(
        Guid canonicalCompanyId,
        Guid duplicateCompanyId,
        CancellationToken cancellationToken)
    {
        var canonical = await dbContext.CompanyMonitoringSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(setting => setting.CompanyId == canonicalCompanyId, cancellationToken);
        var duplicate = await dbContext.CompanyMonitoringSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(setting => setting.CompanyId == duplicateCompanyId, cancellationToken);

        if (duplicate is null)
        {
            return;
        }

        if (canonical is null)
        {
            dbContext.CompanyMonitoringSettings.Add(new CompanyMonitoringSetting
            {
                CompanyId = canonicalCompanyId,
                Enabled = duplicate.Enabled,
                Cadence = duplicate.Cadence,
                NextRunAt = duplicate.NextRunAt,
                LastRunAt = duplicate.LastRunAt,
                LastRunStatus = duplicate.LastRunStatus,
                ActiveClaimId = duplicate.ActiveClaimId,
                ClaimExpiresAt = duplicate.ClaimExpiresAt,
                CreatedAt = duplicate.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await dbContext.CompanyMonitoringSettings
            .Where(setting => setting.CompanyId == duplicateCompanyId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task ReassignCompanyAsync<TEntity>(
        DbSet<TEntity> set,
        Expression<Func<TEntity, Guid>> companySelector,
        Guid canonicalCompanyId,
        Guid duplicateCompanyId,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        await set
            .Where(BuildCompanyPredicate(companySelector, duplicateCompanyId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(companySelector, canonicalCompanyId), cancellationToken);
    }

    private static Expression<Func<TEntity, bool>> BuildCompanyPredicate<TEntity>(
        Expression<Func<TEntity, Guid>> companySelector,
        Guid duplicateCompanyId)
        where TEntity : class
    {
        var parameter = companySelector.Parameters[0];
        var equals = Expression.Equal(companySelector.Body, Expression.Constant(duplicateCompanyId));
        return Expression.Lambda<Func<TEntity, bool>>(equals, parameter);
    }

    private static async Task<int> CountByRunIdsAsync<TEntity>(
        DbSet<TEntity> set,
        Expression<Func<TEntity, Guid?>> runSelector,
        IReadOnlyCollection<Guid> runIds,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (runIds.Count == 0)
        {
            return 0;
        }

        return await set.CountAsync(
            BuildNullableIdPredicate(runSelector, runIds),
            cancellationToken);
    }

    private static async Task<int> CountByIdsAsync<TEntity>(
        DbSet<TEntity> set,
        Expression<Func<TEntity, Guid>> idSelector,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        return await set.CountAsync(
            BuildIdPredicate(idSelector, ids),
            cancellationToken);
    }

    private static Expression<Func<TEntity, bool>> BuildNullableIdPredicate<TEntity>(
        Expression<Func<TEntity, Guid?>> idSelector,
        IReadOnlyCollection<Guid> ids)
        where TEntity : class
    {
        var parameter = idSelector.Parameters[0];
        var value = Expression.Property(idSelector.Body, nameof(Nullable<Guid>.Value));
        var body = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(Guid)],
            Expression.Constant(ids.ToArray()),
            value);
        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    private static Expression<Func<TEntity, bool>> BuildIdPredicate<TEntity>(
        Expression<Func<TEntity, Guid>> idSelector,
        IReadOnlyCollection<Guid> ids)
        where TEntity : class
    {
        var parameter = idSelector.Parameters[0];
        var body = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(Guid)],
            Expression.Constant(ids.ToArray()),
            idSelector.Body);
        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }
}
