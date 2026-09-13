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
using Raven.Api.Features.Research.SavedArtifacts;

namespace Raven.Api.Features.Companies;

/// <summary>
/// Performs user-confirmed workspace lifecycle operations. All destructive
/// operations use one database transaction and explicitly handle the restrictive
/// foreign keys used by the research model.
/// </summary>
public sealed class CompanyLifecycleService(RavenDbContext dbContext) : ICompanyLifecycleService
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
            var deletedRecords = await DeleteCompanyRowsAsync(companyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CompanyDeleteResult(CompanyDeleteOutcome.Deleted, deletedRecords);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<CompanyMergePreviewResponse?> PreviewMergeAsync(
        CompanyMergePreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CanonicalCompanyId == Guid.Empty ||
            request.DuplicateCompanyId == Guid.Empty ||
            request.CanonicalCompanyId == request.DuplicateCompanyId)
        {
            return null;
        }

        var companies = await dbContext.Companies
            .AsNoTracking()
            .Where(item => item.Id == request.CanonicalCompanyId || item.Id == request.DuplicateCompanyId)
            .ToListAsync(cancellationToken);
        var canonical = companies.SingleOrDefault(item => item.Id == request.CanonicalCompanyId);
        var duplicate = companies.SingleOrDefault(item => item.Id == request.DuplicateCompanyId);
        return canonical is null || duplicate is null
            ? null
            : await BuildPreviewAsync(canonical, duplicate, cancellationToken);
    }

    public async Task<CompanyMergeResult> ConfirmMergeAsync(
        CompanyMergeConfirmRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CanonicalCompanyId == Guid.Empty ||
            request.DuplicateCompanyId == Guid.Empty ||
            request.CanonicalCompanyId == request.DuplicateCompanyId)
        {
            return new CompanyMergeResult(
                CompanyMergeOutcome.Invalid,
                Error: "Canonical and duplicate companies must be different valid IDs.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var companies = await dbContext.Companies
                .AsNoTracking()
                .Where(item => item.Id == request.CanonicalCompanyId || item.Id == request.DuplicateCompanyId)
                .ToListAsync(cancellationToken);
            var canonical = companies.SingleOrDefault(item => item.Id == request.CanonicalCompanyId);
            var duplicate = companies.SingleOrDefault(item => item.Id == request.DuplicateCompanyId);
            if (canonical is null || duplicate is null)
            {
                return new CompanyMergeResult(CompanyMergeOutcome.NotFound);
            }

            var preview = await BuildPreviewAsync(canonical, duplicate, cancellationToken);
            if (!request.Confirm)
            {
                return new CompanyMergeResult(
                    CompanyMergeOutcome.ConfirmationRequired,
                    Preview: preview,
                    Error: "The merge must be explicitly confirmed.");
            }

            var duplicateSources = await dbContext.SourceDocuments
                .AsNoTracking()
                .Where(source => source.CompanyId == duplicate.Id)
                .ToListAsync(cancellationToken);
            var canonicalSources = await dbContext.SourceDocuments
                .AsNoTracking()
                .Where(source => source.CompanyId == canonical.Id)
                .ToListAsync(cancellationToken);
            var sourceMap = BuildSourceMap(duplicateSources, canonicalSources);

            var duplicateProfileCandidateIds = await dbContext.CompanyProfileCandidates
                .AsNoTracking()
                .Where(profile => profile.CompanyId == duplicate.Id)
                .Select(profile => profile.Id)
                .ToArrayAsync(cancellationToken);

            await RewriteSourceReferencesAsync(sourceMap, cancellationToken);
            await ReconcileMonitoringAsync(canonical.Id, duplicate.Id, cancellationToken);
            var profileVersionMerge = await PrepareProfileVersionMergeAsync(canonical.Id, duplicate.Id, cancellationToken);

            await ReassignCompanyAsync(dbContext.ResearchRuns, run => run.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.SourceDocuments, source => source.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.CompanyProfileCandidates, profile => profile.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.CompanyProfileVersions, profile => profile.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.ProfileChanges, change => change.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.DeepResearchRuns, run => run.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await ReassignCompanyAsync(dbContext.SavedResearchArtifacts, artifact => artifact.CompanyId, canonical.Id, duplicate.Id, cancellationToken);
            await RewriteMovedProfilePayloadsAsync(
                canonical.Id,
                duplicateProfileCandidateIds,
                profileVersionMerge,
                cancellationToken);

            var sourceIdsToDelete = sourceMap.Keys.ToArray();
            if (sourceIdsToDelete.Length > 0)
            {
                await dbContext.SourceDocuments
                    .Where(source => source.Id != Guid.Empty && sourceIdsToDelete.Contains(source.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            var mergedAt = DateTimeOffset.UtcNow;
            var mergedWebsite = PreferCanonicalValue(canonical.Website, duplicate.Website);
            var mergedCountry = PreferCanonicalValue(canonical.Country, duplicate.Country);
            var mergedLegalName = PreferCanonicalValue(canonical.LegalName, duplicate.LegalName);
            var mergedRegistrationNumber = PreferCanonicalValue(canonical.RegistrationNumber, duplicate.RegistrationNumber);
            var mergedHeadquarters = PreferCanonicalValue(canonical.Headquarters, duplicate.Headquarters);
            var mergedLastResearchedAt = canonical.LastResearchedAt is null
                ? duplicate.LastResearchedAt
                : duplicate.LastResearchedAt is null || canonical.LastResearchedAt >= duplicate.LastResearchedAt
                    ? canonical.LastResearchedAt
                    : duplicate.LastResearchedAt;
            await dbContext.Companies
                .Where(company => company.Id == canonical.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(company => company.Website, mergedWebsite)
                    .SetProperty(company => company.Country, mergedCountry)
                    .SetProperty(company => company.LegalName, mergedLegalName)
                    .SetProperty(company => company.RegistrationNumber, mergedRegistrationNumber)
                    .SetProperty(company => company.Headquarters, mergedHeadquarters)
                    .SetProperty(company => company.LastResearchedAt, mergedLastResearchedAt)
                    .SetProperty(company => company.UpdatedAt, mergedAt), cancellationToken);
            await dbContext.Companies
                .Where(company => company.Id == duplicate.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            canonical.Website = mergedWebsite;
            canonical.Country = mergedCountry;
            canonical.LegalName = mergedLegalName;
            canonical.RegistrationNumber = mergedRegistrationNumber;
            canonical.Headquarters = mergedHeadquarters;
            canonical.LastResearchedAt = mergedLastResearchedAt;
            canonical.UpdatedAt = mergedAt;
            return new CompanyMergeResult(
                CompanyMergeOutcome.Merged,
                ToResponse(canonical),
                preview);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<int> DeleteCompanyRowsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var runIds = await dbContext.ResearchRuns
            .Where(run => run.CompanyId == companyId)
            .Select(run => run.Id)
            .ToArrayAsync(cancellationToken);
        var deepResearchRunIds = await dbContext.DeepResearchRuns
            .Where(run => run.CompanyId == companyId)
            .Select(run => run.Id)
            .ToArrayAsync(cancellationToken);
        var profileCandidateIds = await dbContext.CompanyProfileCandidates
            .Where(profile => profile.CompanyId == companyId)
            .Select(profile => profile.Id)
            .ToArrayAsync(cancellationToken);
        var profileVersionIds = await dbContext.CompanyProfileVersions
            .Where(profile => profile.CompanyId == companyId)
            .Select(profile => profile.Id)
            .ToArrayAsync(cancellationToken);

        var deleted = 0;
        deleted += await dbContext.ProfileEvidences
            .Where(evidence =>
                (evidence.CompanyProfileCandidateId.HasValue && profileCandidateIds.Contains(evidence.CompanyProfileCandidateId.Value)) ||
                (evidence.CompanyProfileVersionId.HasValue && profileVersionIds.Contains(evidence.CompanyProfileVersionId.Value)))
            .ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.ProfileChanges
            .Where(change => change.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.CompanyProfileCandidates
            .Where(profile => profile.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.CompanyProfileVersions
            .Where(profile => profile.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken);

        if (runIds.Length > 0)
        {
            deleted += await dbContext.ResearchEvents
                .Where(researchEvent => researchEvent.ResearchRunId.HasValue && runIds.Contains(researchEvent.ResearchRunId.Value))
                .ExecuteDeleteAsync(cancellationToken);
            deleted += await dbContext.ResearchIdentityCandidates
                .Where(candidate => runIds.Contains(candidate.ResearchRunId))
                .ExecuteDeleteAsync(cancellationToken);
            deleted += await dbContext.ResearchCandidates
                .Where(candidate => runIds.Contains(candidate.ResearchRunId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        deleted += await dbContext.SourceDocuments
            .Where(source => source.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.SavedResearchArtifacts
            .Where(artifact => artifact.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken);

        if (deepResearchRunIds.Length > 0)
        {
            deleted += await dbContext.DeepResearchActivities
                .Where(activity => deepResearchRunIds.Contains(activity.DeepResearchRunId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        deleted += await dbContext.DeepResearchRuns
            .Where(run => run.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.CompanyMonitoringSettings
            .Where(setting => setting.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.ResearchRuns
            .Where(run => run.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken);
        deleted += await dbContext.Companies
            .Where(company => company.Id == companyId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted;
    }

    private async Task<CompanyMergePreviewResponse> BuildPreviewAsync(
        Company canonical,
        Company duplicate,
        CancellationToken cancellationToken)
    {
        var duplicateRunIds = await dbContext.ResearchRuns
            .AsNoTracking()
            .Where(run => run.CompanyId == duplicate.Id)
            .Select(run => run.Id)
            .ToArrayAsync(cancellationToken);
        var duplicateDeepRunIds = await dbContext.DeepResearchRuns
            .AsNoTracking()
            .Where(run => run.CompanyId == duplicate.Id)
            .Select(run => run.Id)
            .ToArrayAsync(cancellationToken);
        var duplicateProfileCandidateIds = await dbContext.CompanyProfileCandidates
            .AsNoTracking()
            .Where(profile => profile.CompanyId == duplicate.Id)
            .Select(profile => profile.Id)
            .ToArrayAsync(cancellationToken);
        var duplicateProfileVersionIds = await dbContext.CompanyProfileVersions
            .AsNoTracking()
            .Where(profile => profile.CompanyId == duplicate.Id)
            .Select(profile => profile.Id)
            .ToArrayAsync(cancellationToken);
        var duplicateSources = await dbContext.SourceDocuments
            .AsNoTracking()
            .Where(source => source.CompanyId == duplicate.Id)
            .ToListAsync(cancellationToken);
        var canonicalSources = await dbContext.SourceDocuments
            .AsNoTracking()
            .Where(source => source.CompanyId == canonical.Id)
            .ToListAsync(cancellationToken);
        var sourceMap = BuildSourceMap(duplicateSources, canonicalSources);

        return new CompanyMergePreviewResponse(
            canonical.Id,
            duplicate.Id,
            canonical.Name,
            duplicate.Name,
            duplicateRunIds.Length,
            await CountByRunIdsAsync(dbContext.ResearchCandidates, candidate => candidate.ResearchRunId, duplicateRunIds, cancellationToken),
            await CountByRunIdsAsync(dbContext.ResearchEvents, researchEvent => researchEvent.ResearchRunId, duplicateRunIds, cancellationToken),
            await CountByRunIdsAsync(dbContext.ResearchIdentityCandidates, candidate => candidate.ResearchRunId, duplicateRunIds, cancellationToken),
            duplicateSources.Count,
            sourceMap.Count,
            duplicateSources.Count - sourceMap.Count,
            duplicateProfileCandidateIds.Length,
            duplicateProfileVersionIds.Length,
            await dbContext.ProfileEvidences.CountAsync(evidence =>
                (evidence.CompanyProfileCandidateId.HasValue && duplicateProfileCandidateIds.Contains(evidence.CompanyProfileCandidateId.Value)) ||
                (evidence.CompanyProfileVersionId.HasValue && duplicateProfileVersionIds.Contains(evidence.CompanyProfileVersionId.Value)), cancellationToken),
            await dbContext.ProfileChanges.CountAsync(change => change.CompanyId == duplicate.Id, cancellationToken),
            duplicateDeepRunIds.Length,
            await CountByIdsAsync(dbContext.DeepResearchActivities, activity => activity.DeepResearchRunId, duplicateDeepRunIds, cancellationToken),
            await dbContext.SavedResearchArtifacts.CountAsync(artifact => artifact.CompanyId == duplicate.Id, cancellationToken),
            await dbContext.CompanyMonitoringSettings.AnyAsync(setting => setting.CompanyId == canonical.Id, cancellationToken),
            await dbContext.CompanyMonitoringSettings.AnyAsync(setting => setting.CompanyId == duplicate.Id, cancellationToken),
            BuildWarnings(sourceMap.Count, duplicateSources.Count - sourceMap.Count,
                await dbContext.CompanyMonitoringSettings.AnyAsync(setting => setting.CompanyId == canonical.Id, cancellationToken),
                await dbContext.CompanyMonitoringSettings.AnyAsync(setting => setting.CompanyId == duplicate.Id, cancellationToken)));
    }

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

    private async Task<ProfileVersionMergePlan> PrepareProfileVersionMergeAsync(
        Guid canonicalCompanyId,
        Guid duplicateCompanyId,
        CancellationToken cancellationToken)
    {
        var canonicalProfiles = await dbContext.CompanyProfileVersions
            .AsNoTracking()
            .Where(profile => profile.CompanyId == canonicalCompanyId)
            .OrderBy(profile => profile.Version)
            .ThenBy(profile => profile.Id)
            .Select(profile => new { profile.Id, profile.Version, profile.ConfirmedAt, profile.GeneratedAt })
            .ToListAsync(cancellationToken);
        var duplicateProfiles = await dbContext.CompanyProfileVersions
            .AsNoTracking()
            .Where(profile => profile.CompanyId == duplicateCompanyId)
            .OrderBy(profile => profile.Version)
            .ThenBy(profile => profile.Id)
            .Select(profile => new { profile.Id, profile.Version, profile.ConfirmedAt, profile.GeneratedAt })
            .ToListAsync(cancellationToken);
        if (duplicateProfiles.Count == 0)
        {
            return new ProfileVersionMergePlan(
                new Dictionary<Guid, int>(),
                new HashSet<Guid>());
        }

        // Move both sets out of the positive version range before assigning the
        // merged history. Version order follows the original accepted snapshot
        // timestamps, not which record happened to be selected as canonical, so
        // a richer/newer duplicate profile cannot be hidden by an older sparse
        // profile. Every changed version is mirrored in ProfileJson because
        // hydration validates the serialized snapshot metadata.
        var temporaryVersion = -1;
        foreach (var profile in duplicateProfiles)
        {
            await dbContext.CompanyProfileVersions
                .Where(item => item.Id == profile.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, temporaryVersion--), cancellationToken);
        }
        foreach (var profile in canonicalProfiles)
        {
            await dbContext.CompanyProfileVersions
                .Where(item => item.Id == profile.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, temporaryVersion--), cancellationToken);
        }

        var nextVersion = 1;
        var assignedVersions = new Dictionary<Guid, int>(duplicateProfiles.Count + canonicalProfiles.Count);
        foreach (var profile in canonicalProfiles
                     .Concat(duplicateProfiles)
                     .OrderBy(profile => profile.ConfirmedAt)
                     .ThenBy(profile => profile.GeneratedAt)
                     .ThenBy(profile => profile.Id))
        {
            var assignedVersion = nextVersion++;
            assignedVersions[profile.Id] = assignedVersion;
            await dbContext.CompanyProfileVersions
                .Where(item => item.Id == profile.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, assignedVersion), cancellationToken);
        }

        return new ProfileVersionMergePlan(
            assignedVersions,
            duplicateProfiles.Select(profile => profile.Id).ToHashSet());
    }

    private async Task RewriteMovedProfilePayloadsAsync(
        Guid canonicalCompanyId,
        IReadOnlyCollection<Guid> movedProfileCandidateIds,
        ProfileVersionMergePlan profileVersionMerge,
        CancellationToken cancellationToken)
    {
        if (movedProfileCandidateIds.Count > 0)
        {
            var candidates = await dbContext.CompanyProfileCandidates
                .Where(profile => movedProfileCandidateIds.Contains(profile.Id))
                .ToListAsync(cancellationToken);
            foreach (var candidate in candidates)
            {
                var rewritten = RewritePayloadReference(candidate.CandidateJson, "companyId", canonicalCompanyId);
                if (rewritten is not null)
                {
                    candidate.CandidateJson = rewritten;
                }
            }
        }

        if (profileVersionMerge.AssignedVersions.Count == 0)
        {
            return;
        }

        var profiles = await dbContext.CompanyProfileVersions
            .Where(profile => profileVersionMerge.AssignedVersions.Keys.Contains(profile.Id))
            .ToListAsync(cancellationToken);
        foreach (var profile in profiles)
        {
            var rewritten = profileVersionMerge.MovedProfileIds.Contains(profile.Id)
                ? RewritePayloadReference(profile.ProfileJson, "companyId", canonicalCompanyId)
                : null;
            rewritten = RewritePayloadReference(
                rewritten ?? profile.ProfileJson,
                "version",
                profileVersionMerge.AssignedVersions[profile.Id]);
            if (rewritten is not null)
            {
                profile.ProfileJson = rewritten;
            }
        }
    }

    private static string? RewritePayloadReference<TValue>(
        string? json,
        string propertyName,
        TValue value)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject payload)
            {
                return null;
            }

            var property = payload.FirstOrDefault(item =>
                string.Equals(item.Key, propertyName, StringComparison.OrdinalIgnoreCase)).Key;
            payload[property ?? propertyName] = JsonValue.Create(value);
            return payload.ToJsonString();
        }
        catch (JsonException)
        {
            // Keep the original payload intact. Hydration already rejects a
            // malformed snapshot, and replacing it would lose user evidence.
            return null;
        }
    }

    private static string? PreferCanonicalValue(string? canonicalValue, string? duplicateValue) =>
        !string.IsNullOrWhiteSpace(canonicalValue) ? canonicalValue : duplicateValue;

    private sealed record ProfileVersionMergePlan(
        IReadOnlyDictionary<Guid, int> AssignedVersions,
        IReadOnlySet<Guid> MovedProfileIds);

    private async Task RewriteSourceReferencesAsync(
        IReadOnlyDictionary<Guid, Guid> sourceMap,
        CancellationToken cancellationToken)
    {
        if (sourceMap.Count == 0)
        {
            return;
        }

        var evidenceRows = await dbContext.ProfileEvidences.ToListAsync(cancellationToken);
        foreach (var evidence in evidenceRows)
        {
            var rewritten = RewriteSourceIdJson(evidence.SourceDocumentIdsJson, sourceMap);
            if (rewritten is not null)
            {
                evidence.SourceDocumentIdsJson = rewritten;
            }
        }

        var artifacts = await dbContext.SavedResearchArtifacts.ToListAsync(cancellationToken);
        foreach (var artifact in artifacts)
        {
            var rewritten = RewriteSourceIdJson(artifact.SourceDocumentIdsJson, sourceMap);
            if (rewritten is not null)
            {
                artifact.SourceDocumentIdsJson = rewritten;
            }
        }

        var activities = await dbContext.DeepResearchActivities.ToListAsync(cancellationToken);
        foreach (var activity in activities)
        {
            var rewritten = RewriteSourceIdJson(activity.SourceDocumentIdsJson, sourceMap);
            if (rewritten is not null)
            {
                dbContext.Entry(activity)
                    .Property(item => item.SourceDocumentIdsJson)
                    .CurrentValue = rewritten;
            }
        }
    }

    private static string? RewriteSourceIdJson(
        string? json,
        IReadOnlyDictionary<Guid, Guid> sourceMap)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var ids = JsonSerializer.Deserialize<Guid[]>(json);
            if (ids is null)
            {
                return null;
            }

            var rewritten = ids
                .Select(id => sourceMap.TryGetValue(id, out var replacement) ? replacement : id)
                .Distinct()
                .ToArray();
            return ids.SequenceEqual(rewritten) ? null : JsonSerializer.Serialize(rewritten);
        }
        catch (JsonException)
        {
            // Corrupt optional provenance should not make a company impossible to
            // merge. The original JSON is retained and the source row still moves.
            return null;
        }
    }

    private static IReadOnlyDictionary<Guid, Guid> BuildSourceMap(
        IReadOnlyCollection<SourceDocument> duplicateSources,
        IReadOnlyCollection<SourceDocument> canonicalSources)
    {
        var byUrl = canonicalSources
            .Where(source => !string.IsNullOrWhiteSpace(source.NormalizedUrl))
            .GroupBy(source => source.NormalizedUrl, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(source => source.Id).First().Id, StringComparer.OrdinalIgnoreCase);
        var byHash = canonicalSources
            .Where(source => !string.IsNullOrWhiteSpace(source.ContentHash))
            .GroupBy(source => source.ContentHash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(source => source.Id).First().Id, StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<Guid, Guid>();
        foreach (var source in duplicateSources.OrderBy(source => source.Id))
        {
            if (!string.IsNullOrWhiteSpace(source.NormalizedUrl) && byUrl.TryGetValue(source.NormalizedUrl, out var urlMatch))
            {
                map[source.Id] = urlMatch;
            }
            else if (!string.IsNullOrWhiteSpace(source.ContentHash) && byHash.TryGetValue(source.ContentHash, out var hashMatch))
            {
                map[source.Id] = hashMatch;
            }
        }

        return map;
    }

    private static IReadOnlyList<string> BuildWarnings(
        int reusableSources,
        int movableSources,
        bool hasCanonicalMonitoring,
        bool hasDuplicateMonitoring)
    {
        var warnings = new List<string>();
        if (reusableSources > 0)
        {
            warnings.Add($"{reusableSources} duplicate source document(s) match canonical URL/content and will be deduplicated.");
        }

        if (movableSources > 0)
        {
            warnings.Add($"{movableSources} source document(s) will be reassigned to the canonical company.");
        }

        if (hasCanonicalMonitoring && hasDuplicateMonitoring)
        {
            warnings.Add("The canonical company's monitoring setting will be retained; the duplicate setting will be discarded.");
        }
        else if (hasDuplicateMonitoring)
        {
            warnings.Add("The duplicate monitoring setting will be moved to the canonical company.");
        }

        return warnings;
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
