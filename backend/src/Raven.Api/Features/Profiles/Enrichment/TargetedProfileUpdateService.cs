using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Profiles.Generation;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.Planning;

namespace Raven.Api.Features.Profiles.Enrichment;

/// <summary>
/// Converts evidence from a targeted normal research run into a patch-shaped
/// review candidate. Only target-authorized fields can change; all other
/// accepted profile values and provenance are copied verbatim into the next
/// immutable version.
/// </summary>
public sealed class TargetedProfileUpdateService(
    RavenDbContext dbContext,
    IResearchCompanyService research,
    ICompanyProfilePersistenceService persistence,
    ICompanyProfileWorkflowService workflow,
    IProfileGenerationService generation) : ITargetedProfileUpdateService
{
    public async Task<ResearchRunResponse?> StartAsync(
        Guid companyId,
        StartTargetedResearchRequest request,
        CancellationToken cancellationToken)
    {
        var targets = (request.Targets ?? []).Distinct().Take(TargetedQueryPlanner.MaximumTargetsPerRound).ToArray();
        if (targets.Length == 0)
        {
            throw new BadHttpRequestException("Select at least one research target.");
        }

        var profiles = await persistence.ListProfileVersionsAsync(companyId, cancellationToken);
        var baseProfile = request.BaseProfileVersionId is { } baseId
            ? profiles.SingleOrDefault(profile => profile.Id == baseId)
            : profiles.OrderByDescending(profile => profile.Version).FirstOrDefault();
        if (baseProfile is null)
        {
            throw new BadHttpRequestException("Targeted enrichment requires an accepted base Company Profile.");
        }

        return await research.DiscoverAsync(companyId, new DiscoverResearchRequest(
            UseAcceptedProfileIdentity: true,
            Mode: ResearchMode.TargetedEnrichment,
            BaseProfileVersionId: baseProfile.Id,
            Targets: targets), cancellationToken);
    }

    public async Task<ProfilePatchCandidate?> GenerateAsync(Guid researchRunId, CancellationToken cancellationToken)
    {
        var run = await dbContext.ResearchRuns.SingleOrDefaultAsync(item => item.Id == researchRunId, cancellationToken);
        if (run is null || run.Mode != ResearchMode.TargetedEnrichment || run.BaseProfileVersionId is null)
        {
            return null;
        }

        if (run.Stage != ResearchStage.EvidenceReady)
        {
            throw new BadHttpRequestException("Acquire targeted evidence before generating a profile patch.");
        }

        var targets = DeserializeTargets(run.ResearchTargetsJson);
        if (targets.Count == 0)
        {
            throw new BadHttpRequestException("This targeted run has no permitted research targets.");
        }

        var baseProfile = (await persistence.ListProfileVersionsAsync(run.CompanyId, cancellationToken))
            .SingleOrDefault(profile => profile.Id == run.BaseProfileVersionId.Value);
        if (baseProfile is null)
        {
            throw new BadHttpRequestException("The selected base profile is unavailable.");
        }

        var sources = await dbContext.SourceDocuments.Where(source => source.ResearchRunId == run.Id).ToListAsync(cancellationToken);
        if (sources.Count == 0)
        {
            throw new BadHttpRequestException("This targeted run has no acquired evidence.");
        }

        var sourceIds = sources.Select(source => source.Id).ToHashSet();
        var result = await generation.GenerateAsync(new ProfileGenerationInput(
            run.CompanyId,
            run.Id,
            new ProfileIdentityHints(baseProfile.DisplayName, baseProfile.LegalName, baseProfile.Website,
                baseProfile.Country, baseProfile.Headquarters, baseProfile.RegistrationNumberOrTaxId),
            sources,
            new ProfileValidationContext(run.CompanyId, run.Id, sourceIds, sourceIds)), cancellationToken);
        if (!result.Succeeded || result.Candidate is null)
        {
            return new ProfilePatchCandidate(Guid.Empty, run.Id, baseProfile.Id, targets, [],
                result.Warnings.Concat([result.Failure?.Message ?? "No supported profile patch was generated."]).Distinct().ToArray());
        }

        var merged = Merge(baseProfile, result.Candidate, targets, out var changes, out var warnings);
        if (changes.Count == 0)
        {
            return new ProfilePatchCandidate(Guid.Empty, run.Id, baseProfile.Id, targets, [], warnings);
        }

        var saved = await persistence.SaveCandidateAsync(merged, cancellationToken);
        if (saved is null)
        {
            return new ProfilePatchCandidate(Guid.Empty, run.Id, baseProfile.Id, targets, [],
                warnings.Concat(["The server rejected the patch evidence."]).ToArray());
        }

        run.Stage = ResearchStage.AwaitingProfileConfirmation;
        run.Status = ResearchRunStatus.Completed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ProfilePatchCandidate(saved.Id, run.Id, baseProfile.Id, targets, changes, warnings);
    }

    public Task<CompanyProfileVersion?> ConfirmAsync(Guid researchRunId, Guid candidateId, CancellationToken cancellationToken) =>
        workflow.ConfirmAsync(researchRunId, candidateId, cancellationToken);

    private static CompanyProfileCandidate Merge(
        CompanyProfileVersion baseline,
        CompanyProfileCandidate proposed,
        IReadOnlyCollection<ResearchTarget> targets,
        out IReadOnlyList<ProfilePatchChange> changes,
        out IReadOnlyList<string> warnings)
    {
        var merged = CopyBaseline(baseline, proposed);
        var allChanges = new List<ProfilePatchChange>();
        var allWarnings = proposed.ValidationWarnings.ToList();
        foreach (var target in targets)
        {
            if (!HasEvidence(proposed, target))
            {
                allWarnings.Add($"{target} remains unknown because the targeted evidence did not support a patch.");
                continue;
            }

            ApplyTarget(merged, proposed, baseline, target, allChanges);
            ReplaceEvidence(merged, proposed, target);
        }

        changes = allChanges
            .Select(change => change with
            {
                EvidenceSourceDocumentIds = proposed.Evidence
                    .Where(evidence => EvidenceAppliesToChange(evidence.FieldPath, change.FieldPath))
                    .SelectMany(evidence => evidence.SourceDocumentIds)
                    .Distinct()
                    .ToArray()
            })
            .ToArray();
        warnings = allWarnings.Distinct(StringComparer.Ordinal).ToArray();
        return merged;
    }

    private static CompanyProfileCandidate CopyBaseline(CompanyProfileVersion value, CompanyProfileCandidate metadata)
    {
        var copy = new CompanyProfileCandidate
        {
            CompanyId = value.CompanyId,
            ResearchRunId = metadata.ResearchRunId,
            AiProvider = metadata.AiProvider,
            AiModel = metadata.AiModel,
            PromptTemplateVersion = metadata.PromptTemplateVersion,
            DisplayName = value.DisplayName,
            LegalName = value.LegalName,
            Website = value.Website,
            Country = value.Country,
            Headquarters = value.Headquarters,
            RegistrationNumberOrTaxId = value.RegistrationNumberOrTaxId,
            FoundedYear = value.FoundedYear,
            PrimaryIndustry = value.PrimaryIndustry,
            CompanySize = value.CompanySize,
            EmployeeCount = value.EmployeeCount,
            EmployeeCountRange = value.EmployeeCountRange,
            Summary = value.Summary
        };
        foreach (var item in value.SecondaryIndustries) copy.SecondaryIndustries.Add(item);
        foreach (var item in value.ProductsServices) copy.ProductsServices.Add(item with { });
        foreach (var item in value.Markets) copy.Markets.Add(item with { });
        foreach (var item in value.Leadership) copy.Leadership.Add(item with { });
        foreach (var item in value.Locations) copy.Locations.Add(item with { });
        foreach (var item in value.PublicLinks) copy.PublicLinks.Add(item with { });
        foreach (var evidence in value.Evidence)
        {
            copy.Evidence.Add(NewEvidence(evidence.FieldPath, evidence.SourceDocumentIds));
        }
        return copy;
    }

    private static void ApplyTarget(CompanyProfileCandidate target, CompanyProfileCandidate proposed,
        CompanyProfileVersion baseline, ResearchTarget researchTarget, ICollection<ProfilePatchChange> changes)
    {
        switch (researchTarget)
        {
            case ResearchTarget.LegalIdentity:
                Set("displayName", baseline.DisplayName, proposed.DisplayName, value => target.DisplayName = value, changes);
                Set("legalName", baseline.LegalName, proposed.LegalName, value => target.LegalName = value, changes);
                Set("website", baseline.Website, proposed.Website, value => target.Website = value, changes);
                Set("country", baseline.Country, proposed.Country, value => target.Country = value, changes);
                Set("headquarters", baseline.Headquarters, proposed.Headquarters, value => target.Headquarters = value, changes);
                break;
            case ResearchTarget.TaxRegistration:
                Set("registrationNumberOrTaxId", baseline.RegistrationNumberOrTaxId, proposed.RegistrationNumberOrTaxId, value => target.RegistrationNumberOrTaxId = value, changes);
                break;
            case ResearchTarget.FoundedHistory:
                Set("foundedYear", baseline.FoundedYear?.ToString(), proposed.FoundedYear?.ToString(), value => target.FoundedYear = int.TryParse(value, out var year) ? year : null, changes);
                break;
            case ResearchTarget.Industry:
                Set("primaryIndustry", baseline.PrimaryIndustry, proposed.PrimaryIndustry, value => target.PrimaryIndustry = value, changes);
                ReplaceCollection("secondaryIndustries", target.SecondaryIndustries, proposed.SecondaryIndustries, changes);
                break;
            case ResearchTarget.EmployeeScale:
                Set("companySize", baseline.CompanySize, proposed.CompanySize, value => target.CompanySize = value, changes);
                Set("employeeCount", baseline.EmployeeCount?.ToString(), proposed.EmployeeCount?.ToString(), value => target.EmployeeCount = int.TryParse(value, out var count) ? count : null, changes);
                Set("employeeCountRange", baseline.EmployeeCountRange, proposed.EmployeeCountRange, value => target.EmployeeCountRange = value, changes);
                break;
            case ResearchTarget.ProductsServices:
                ReplaceCollection("productsServices", target.ProductsServices, proposed.ProductsServices, changes);
                break;
            case ResearchTarget.Markets:
                ReplaceCollection("markets", target.Markets, proposed.Markets, changes);
                break;
            case ResearchTarget.Leadership:
                ReplaceCollection("leadership", target.Leadership, proposed.Leadership, changes);
                break;
            case ResearchTarget.Locations:
                ReplaceCollection("locations", target.Locations, proposed.Locations, changes);
                break;
        }
    }

    private static void Set(string field, string? oldValue, string? newValue, Action<string?> setter, ICollection<ProfilePatchChange> changes)
    {
        if (string.IsNullOrWhiteSpace(newValue) || string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;
        setter(newValue);
        changes.Add(new ProfilePatchChange(field, oldValue, newValue, []));
    }

    private static void ReplaceCollection<T>(string field, ICollection<T> destination, ICollection<T> proposed, ICollection<ProfilePatchChange> changes)
    {
        if (proposed.Count == 0) return;
        var oldJson = JsonSerializer.Serialize(destination);
        var newJson = JsonSerializer.Serialize(proposed);
        if (oldJson == newJson) return;
        destination.Clear();
        foreach (var item in proposed) destination.Add(item);
        changes.Add(new ProfilePatchChange(field, oldJson, newJson, []));
    }

    private static bool HasEvidence(CompanyProfileCandidate candidate, ResearchTarget target) =>
        candidate.Evidence.Any(evidence => evidence.SourceDocumentIds.Count > 0 && IsFieldForTarget(evidence.FieldPath, target));

    private static void ReplaceEvidence(CompanyProfileCandidate destination, CompanyProfileCandidate proposed, ResearchTarget target)
    {
        foreach (var evidence in destination.Evidence.Where(evidence => IsFieldForTarget(evidence.FieldPath, target)).ToArray()) destination.Evidence.Remove(evidence);
        foreach (var evidence in proposed.Evidence.Where(evidence => IsFieldForTarget(evidence.FieldPath, target)))
            destination.Evidence.Add(NewEvidence(evidence.FieldPath, evidence.SourceDocumentIds));
    }

    private static ProfileEvidence NewEvidence(string fieldPath, IEnumerable<Guid> sourceIds)
    {
        var evidence = new ProfileEvidence { FieldPath = fieldPath };
        foreach (var sourceId in sourceIds.Distinct()) evidence.SourceDocumentIds.Add(sourceId);
        return evidence;
    }

    private static bool IsFieldForTarget(string path, ResearchTarget target) => target switch
    {
        ResearchTarget.LegalIdentity => path is "displayName" or "legalName" or "website" or "country" or "headquarters",
        ResearchTarget.TaxRegistration => path == "registrationNumberOrTaxId",
        ResearchTarget.FoundedHistory => path == "foundedYear",
        ResearchTarget.Industry => path is "primaryIndustry" or "secondaryIndustries",
        ResearchTarget.EmployeeScale => path is "companySize" or "employeeCount" or "employeeCountRange",
        ResearchTarget.ProductsServices => path.StartsWith("productsServices", StringComparison.Ordinal),
        ResearchTarget.Markets => path.StartsWith("markets", StringComparison.Ordinal),
        ResearchTarget.Leadership => path.StartsWith("leadership", StringComparison.Ordinal),
        ResearchTarget.Locations => path.StartsWith("locations", StringComparison.Ordinal),
        _ => false
    };

    private static bool EvidenceAppliesToChange(string evidencePath, string changedPath) =>
        string.Equals(evidencePath, changedPath, StringComparison.Ordinal) ||
        evidencePath.StartsWith($"{changedPath}.", StringComparison.Ordinal) ||
        changedPath.StartsWith($"{evidencePath}.", StringComparison.Ordinal);

    private static IReadOnlyList<ResearchTarget> DeserializeTargets(string json)
    {
        try { return JsonSerializer.Deserialize<ResearchTarget[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
