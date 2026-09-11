using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Raven.Api.Data;
using Raven.Api.Features.Research;
using Raven.Api.Features.Profiles.Changes;

namespace Raven.Api.Features.Profiles.Persistence;

/// <summary>
/// Persists generated profile previews and immutable, confirmed versions.
/// Candidate/profile snapshots are serialized as bounded JSON because their
/// shape is intentionally extensible; source provenance is additionally stored
/// in ProfileEvidences so it remains queryable and survives rehydration.
/// </summary>
public sealed class CompanyProfilePersistenceService(
    RavenDbContext dbContext,
    IProfileDiffService? profileDiffService = null)
    : ICompanyProfilePersistenceService
{
    private readonly IProfileDiffService profileDiffService = profileDiffService ?? new ProfileDiffService();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate
    };

    public async Task<CompanyProfileCandidate?> SaveCandidateAsync(
        CompanyProfileCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var validationContext = await BuildValidationContextAsync(
            candidate.CompanyId,
            candidate.ResearchRunId,
            cancellationToken);
        if (validationContext is null)
        {
            return null;
        }

        var validation = CompanyProfileValidator.Validate(candidate, validationContext);
        if (!validation.IsValid || !TrySerialize(validation.Candidate, out var candidateJson))
        {
            return null;
        }

        var persisted = await dbContext.CompanyProfileCandidates
            .SingleOrDefaultAsync(profile => profile.Id == validation.Candidate.Id, cancellationToken);

        if (persisted is null)
        {
            persisted = NewCandidateEntity(validation.Candidate, candidateJson);
            dbContext.CompanyProfileCandidates.Add(persisted);
        }
        else
        {
            // The server retains the candidate's identity and research context;
            // only a new generated preview may replace its serialized payload.
            // Init-only metadata cannot be reassigned, so a mismatched replay is
            // rejected rather than turning a candidate into another company's
            // preview.
            if (persisted.CompanyId != validation.Candidate.CompanyId
                || persisted.ResearchRunId != validation.Candidate.ResearchRunId
                || persisted.GeneratedAt != validation.Candidate.GeneratedAt)
            {
                return null;
            }

            persisted.CandidateJson = candidateJson;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return HydrateCandidate(persisted);
    }

    public async Task<CompanyProfileCandidate?> GetCandidateAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        var persisted = await dbContext.CompanyProfileCandidates
            .AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.Id == candidateId, cancellationToken);

        return persisted is null ? null : HydrateCandidate(persisted);
    }

    public async Task<CompanyProfileVersion?> ConfirmCandidateAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        var persistedCandidate = await dbContext.CompanyProfileCandidates
            .AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.Id == candidateId, cancellationToken);
        if (persistedCandidate is null)
        {
            return null;
        }

        var candidate = HydrateCandidate(persistedCandidate);
        if (candidate is null)
        {
            return null;
        }

        var validationContext = await BuildValidationContextAsync(
            persistedCandidate.CompanyId,
            persistedCandidate.ResearchRunId,
            cancellationToken);
        if (validationContext is null)
        {
            return null;
        }

        var validation = CompanyProfileValidator.Validate(candidate, validationContext);
        if (!validation.IsValid || !TrySerialize(validation.Candidate, out _))
        {
            return null;
        }

        // The version allocation and run/profile updates are one unit. The
        // unique company/version index remains the final guard against races.
        IDbContextTransaction? transaction = null;
        if (dbContext.Database.IsRelational())
        {
            transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var company = await dbContext.Companies
                .SingleOrDefaultAsync(item => item.Id == persistedCandidate.CompanyId, cancellationToken);
            var run = await dbContext.ResearchRuns
                .SingleOrDefaultAsync(item => item.Id == persistedCandidate.ResearchRunId, cancellationToken);
            if (company is null || run is null || run.CompanyId != company.Id)
            {
                return null;
            }

            var currentVersion = await dbContext.CompanyProfileVersions
                .Where(profile => profile.CompanyId == company.Id)
                .Select(profile => (int?)profile.Version)
                .MaxAsync(cancellationToken) ?? 0;

            var previousPersisted = currentVersion == 0
                ? null
                : await dbContext.CompanyProfileVersions
                    .AsNoTracking()
                    .Where(profile => profile.CompanyId == company.Id && profile.Version == currentVersion)
                    .SingleOrDefaultAsync(cancellationToken);

            var confirmedAt = DateTimeOffset.UtcNow;
            var accepted = CompanyProfileVersion.FromCandidate(
                validation.Candidate,
                currentVersion + 1,
                confirmedAt);
            if (!TrySerialize(accepted, out var profileJson))
            {
                return null;
            }

            accepted.ProfileJson = profileJson;
            dbContext.CompanyProfileVersions.Add(accepted);

            if (previousPersisted is not null)
            {
                var previous = HydrateVersion(previousPersisted, []);
                if (previous is not null)
                {
                    foreach (var change in profileDiffService.Compare(previous, accepted, confirmedAt))
                    {
                        dbContext.ProfileChanges.Add(change);
                    }
                }
            }

            foreach (var evidence in accepted.Evidence)
            {
                dbContext.ProfileEvidences.Add(new ProfileEvidence
                {
                    Id = evidence.Id,
                    CompanyProfileVersionId = accepted.Id,
                    FieldPath = evidence.FieldPath,
                    SourceDocumentIdsJson = SerializeSourceDocumentIds(evidence.SourceDocumentIds)
                });
            }

            run.Status = ResearchRunStatus.Completed;
            run.Stage = ResearchStage.Completed;
            run.CompletedAt = confirmedAt;
            run.Error = null;
            company.LastResearchedAt = confirmedAt;
            company.UpdatedAt = confirmedAt;

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            var evidenceRows = await dbContext.ProfileEvidences
                .AsNoTracking()
                .Where(evidence => evidence.CompanyProfileVersionId == accepted.Id)
                .ToListAsync(cancellationToken);
            return HydrateVersion(accepted, evidenceRows);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<CompanyProfileVersion?> GetCurrentProfileAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var persisted = await dbContext.CompanyProfileVersions
            .AsNoTracking()
            .Where(profile => profile.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var latest = persisted.OrderByDescending(profile => profile.Version).FirstOrDefault();
        if (latest is null)
        {
            return null;
        }

        var evidenceRows = await dbContext.ProfileEvidences
            .AsNoTracking()
            .Where(evidence => evidence.CompanyProfileVersionId == latest.Id)
            .ToListAsync(cancellationToken);
        return HydrateVersion(latest, evidenceRows);
    }

    public async Task<IReadOnlyList<CompanyProfileVersion>> ListProfileVersionsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var persisted = await dbContext.CompanyProfileVersions
            .AsNoTracking()
            .Where(profile => profile.CompanyId == companyId)
            .OrderByDescending(profile => profile.Version)
            .ToListAsync(cancellationToken);

        return await HydrateVersionsAsync(persisted, cancellationToken);
    }

    public async Task<CompanyProfileVersion?> GetProfileVersionAsync(
        Guid companyId,
        int version,
        CancellationToken cancellationToken = default)
    {
        var persisted = await dbContext.CompanyProfileVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.CompanyId == companyId && profile.Version == version, cancellationToken);
        if (persisted is null)
        {
            return null;
        }

        var evidenceRows = await dbContext.ProfileEvidences
            .AsNoTracking()
            .Where(evidence => evidence.CompanyProfileVersionId == persisted.Id)
            .ToListAsync(cancellationToken);
        return HydrateVersion(persisted, evidenceRows);
    }

    private async Task<IReadOnlyList<CompanyProfileVersion>> HydrateVersionsAsync(
        IReadOnlyList<CompanyProfileVersion> persisted,
        CancellationToken cancellationToken)
    {
        if (persisted.Count == 0)
        {
            return [];
        }

        var profileIds = persisted.Select(profile => profile.Id).ToArray();
        var evidenceByProfile = (await dbContext.ProfileEvidences
            .AsNoTracking()
            .Where(evidence => evidence.CompanyProfileVersionId.HasValue && profileIds.Contains(evidence.CompanyProfileVersionId.Value))
            .ToListAsync(cancellationToken))
            .GroupBy(evidence => evidence.CompanyProfileVersionId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<ProfileEvidence>)group.ToArray());

        return persisted
            .Select(profile => HydrateVersion(
                profile,
                evidenceByProfile.GetValueOrDefault(profile.Id, [])))
            .OfType<CompanyProfileVersion>()
            .ToArray();
    }

    private async Task<ProfileValidationContext?> BuildValidationContextAsync(
        Guid companyId,
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        var companyExists = await dbContext.Companies
            .AsNoTracking()
            .AnyAsync(company => company.Id == companyId, cancellationToken);
        if (!companyExists)
        {
            return null;
        }

        var runBelongsToCompany = await dbContext.ResearchRuns
            .AsNoTracking()
            .AnyAsync(run => run.Id == researchRunId && run.CompanyId == companyId, cancellationToken);
        if (!runBelongsToCompany)
        {
            return null;
        }

        var companySourceDocumentIds = await dbContext.SourceDocuments
            .AsNoTracking()
            .Where(source => source.CompanyId == companyId)
            .Select(source => source.Id)
            .ToHashSetAsync(cancellationToken);
        var researchRunSourceDocumentIds = await dbContext.SourceDocuments
            .AsNoTracking()
            .Where(source => source.ResearchRunId == researchRunId)
            .Select(source => source.Id)
            .ToHashSetAsync(cancellationToken);

        return new ProfileValidationContext(
            companyId,
            researchRunId,
            companySourceDocumentIds,
            researchRunSourceDocumentIds);
    }

    private static CompanyProfileCandidate NewCandidateEntity(
        CompanyProfileCandidate candidate,
        string candidateJson) =>
        new()
        {
            Id = candidate.Id,
            CompanyId = candidate.CompanyId,
            ResearchRunId = candidate.ResearchRunId,
            GeneratedAt = candidate.GeneratedAt,
            AiProvider = candidate.AiProvider,
            AiModel = candidate.AiModel,
            PromptTemplateVersion = candidate.PromptTemplateVersion,
            CandidateJson = candidateJson
        };

    private static CompanyProfileCandidate? HydrateCandidate(CompanyProfileCandidate persisted)
    {
        try
        {
            var candidate = JsonSerializer.Deserialize<CompanyProfileCandidate>(persisted.CandidateJson, JsonOptions);
            if (candidate is null
                || candidate.Id != persisted.Id
                || candidate.CompanyId != persisted.CompanyId
                || candidate.ResearchRunId != persisted.ResearchRunId
                || candidate.GeneratedAt != persisted.GeneratedAt)
            {
                return null;
            }

            candidate.CandidateJson = persisted.CandidateJson;
            return candidate;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static CompanyProfileVersion? HydrateVersion(
        CompanyProfileVersion persisted,
        IReadOnlyCollection<ProfileEvidence> evidenceRows)
    {
        try
        {
            var version = JsonSerializer.Deserialize<CompanyProfileVersion>(persisted.ProfileJson, JsonOptions);
            if (version is null
                || version.Id != persisted.Id
                || version.CompanyId != persisted.CompanyId
                || version.ResearchRunId != persisted.ResearchRunId
                || version.Version != persisted.Version
                || version.GeneratedAt != persisted.GeneratedAt
                || version.ConfirmedAt != persisted.ConfirmedAt)
            {
                return null;
            }

            version.ProfileJson = persisted.ProfileJson;
            if (evidenceRows.Count > 0)
            {
                version.Evidence.Clear();
                foreach (var row in evidenceRows)
                {
                    if (!TryHydrateEvidence(row, out var evidence))
                    {
                        continue;
                    }

                    version.Evidence.Add(evidence);
                }
            }

            return version;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryHydrateEvidence(ProfileEvidence persisted, out ProfileEvidence evidence)
    {
        evidence = new ProfileEvidence
        {
            Id = persisted.Id,
            CompanyProfileVersionId = persisted.CompanyProfileVersionId,
            FieldPath = persisted.FieldPath,
            SourceDocumentIdsJson = persisted.SourceDocumentIdsJson
        };

        try
        {
            var sourceIds = JsonSerializer.Deserialize<Guid[]>(persisted.SourceDocumentIdsJson, JsonOptions);
            if (sourceIds is null)
            {
                return false;
            }

            foreach (var sourceId in sourceIds.Distinct())
            {
                evidence.SourceDocumentIds.Add(sourceId);
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool TrySerialize<T>(T value, out string json)
    {
        try
        {
            json = JsonSerializer.Serialize(value, JsonOptions);
            return !string.IsNullOrWhiteSpace(json);
        }
        catch (JsonException)
        {
            json = string.Empty;
            return false;
        }
        catch (NotSupportedException)
        {
            json = string.Empty;
            return false;
        }
    }

    private static string SerializeSourceDocumentIds(IEnumerable<Guid> sourceDocumentIds) =>
        JsonSerializer.Serialize(sourceDocumentIds.Distinct().ToArray(), JsonOptions);
}
