using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Research.SavedArtifacts;

namespace Raven.Api.Features.Research.Briefings;

public sealed class BriefingService(RavenDbContext db, InvestigationService investigations, BriefingGenerator generator)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<BriefingListItemResponse>> ListAsync(Guid companyId, CancellationToken ct)
    {
        var briefs = await db.ResearchBriefings.AsNoTracking().Where(item => item.CompanyId == companyId && item.ArchivedAt == null).ToListAsync(ct);
        if (briefs.Count == 0) return [];
        var versions = await db.ResearchBriefingVersions.AsNoTracking().Where(item => briefs.Select(brief => brief.Id).Contains(item.BriefingId)).ToListAsync(ct);
        var available = await investigations.ListAsync(companyId, ct);
        return briefs.Select(brief =>
        {
            var current = versions.Where(version => version.BriefingId == brief.Id).MaxBy(version => version.VersionNumber)!;
            var sources = Sources(current);
            return new BriefingListItemResponse(brief.Id, brief.Title, brief.Template, current.GeneratedAt,
                current.ResearchThrough, current.VersionNumber, sources.Length,
                NewerCandidates(brief, current, sources, available).Count);
        }).OrderByDescending(item => item.GeneratedAt).ToArray();
    }

    public async Task<BriefingResponse?> GetAsync(Guid companyId, Guid briefingId, CancellationToken ct)
    {
        var brief = await db.ResearchBriefings.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == briefingId, ct);
        if (brief is null) return null;
        var versions = await db.ResearchBriefingVersions.AsNoTracking().Where(item => item.BriefingId == briefingId).ToListAsync(ct);
        var current = versions.MaxBy(version => version.VersionNumber)!;
        var available = await investigations.ListAsync(companyId, ct);
        return ToResponse(brief, current, versions.Count, NewerCandidates(brief, current, Sources(current), available).Count);
    }

    public async Task<BriefingResponse> CreateAsync(Guid companyId, CreateBriefingRequest request, CancellationToken ct)
    {
        ValidateDefinition(request.Title, request.Template, request.Objective);
        if (!await db.Companies.AsNoTracking().AnyAsync(item => item.Id == companyId, ct))
            throw new KeyNotFoundException("Company not found.");
        var available = await investigations.ListAsync(companyId, ct);
        var sources = SelectSources(available, request.InvestigationIds);
        var sections = await generator.GenerateAsync(request.Template, request.Objective.Trim(), sources, ct);
        var now = DateTimeOffset.UtcNow;
        var brief = new ResearchBriefing
        {
            CompanyId = companyId, Title = request.Title.Trim(), Template = request.Template,
            Objective = request.Objective.Trim(), CreatedAt = now, UpdatedAt = now
        };
        var version = NewVersion(brief, 1, now, sources, sections);
        db.ResearchBriefings.Add(brief);
        db.ResearchBriefingVersions.Add(version);
        await db.SaveChangesAsync(ct);
        return ToResponse(brief, version, 1, 0);
    }

    public async Task<BriefingResponse?> UpdateAsync(Guid companyId, Guid briefingId, UpdateBriefingRequest request, CancellationToken ct)
    {
        var brief = await db.ResearchBriefings.SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == briefingId, ct);
        if (brief is null) return null;
        if (brief.ArchivedAt is not null) throw new InvalidOperationException("Archived briefings cannot be updated.");
        var title = request.Title ?? brief.Title;
        var template = request.Template ?? brief.Template;
        var objective = request.Objective ?? brief.Objective;
        ValidateDefinition(title, template, objective);
        var versions = await db.ResearchBriefingVersions.AsNoTracking().Where(item => item.BriefingId == briefingId).ToListAsync(ct);
        var current = versions.MaxBy(version => version.VersionNumber)!;
        var previousSources = Sources(current);
        var available = await investigations.ListAsync(companyId, ct);
        if (request.NewInvestigationIds is null) throw new ArgumentException("Select Investigations to include.");
        var additions = request.NewInvestigationIds.Count == 0 ? [] : SelectSources(available, request.NewInvestigationIds);
        var sources = previousSources.Where(source => additions.All(addition => addition.InvestigationId != source.InvestigationId))
            .Concat(additions).ToArray();
        if (sources.Length > 12) throw new ArgumentException("A Briefing supports at most 12 Investigations.");
        var sections = await generator.GenerateAsync(template, objective.Trim(), sources, ct);
        var now = DateTimeOffset.UtcNow;
        var version = NewVersion(brief, current.VersionNumber + 1, now, sources, sections, title.Trim(), template, objective.Trim());
        brief.Title = title.Trim();
        brief.Template = template;
        brief.Objective = objective.Trim();
        brief.UpdatedAt = now;
        db.ResearchBriefingVersions.Add(version);
        await db.SaveChangesAsync(ct);
        return ToResponse(brief, version, versions.Count + 1,
            NewerCandidates(brief, version, sources, available).Count);
    }

    public async Task<IReadOnlyList<BriefingVersionResponse>?> VersionsAsync(Guid companyId, Guid briefingId, CancellationToken ct)
    {
        if (!await db.ResearchBriefings.AsNoTracking().AnyAsync(item => item.CompanyId == companyId && item.Id == briefingId, ct)) return null;
        return (await db.ResearchBriefingVersions.AsNoTracking().Where(item => item.BriefingId == briefingId).ToListAsync(ct))
            .OrderByDescending(item => item.VersionNumber).Select(ToVersion).ToArray();
    }

    public async Task<BriefingVersionResponse?> VersionAsync(Guid companyId, Guid briefingId, int number, CancellationToken ct)
    {
        if (!await db.ResearchBriefings.AsNoTracking().AnyAsync(item => item.CompanyId == companyId && item.Id == briefingId, ct)) return null;
        var version = await db.ResearchBriefingVersions.AsNoTracking().SingleOrDefaultAsync(item => item.BriefingId == briefingId && item.VersionNumber == number, ct);
        return version is null ? null : ToVersion(version);
    }

    public async Task<IReadOnlyList<InvestigationResponse>?> NewerAsync(Guid companyId, Guid briefingId, CancellationToken ct)
    {
        var brief = await db.ResearchBriefings.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == briefingId, ct);
        if (brief is null) return null;
        var versions = await db.ResearchBriefingVersions.AsNoTracking().Where(item => item.BriefingId == briefingId).ToListAsync(ct);
        var current = versions.MaxBy(version => version.VersionNumber)!;
        return NewerCandidates(brief, current, Sources(current), await investigations.ListAsync(companyId, ct));
    }

    public async Task<BriefingChangeResponse?> CompareAsync(Guid companyId, Guid briefingId, int toVersion, CancellationToken ct)
    {
        var versions = await VersionsAsync(companyId, briefingId, ct);
        if (versions is null) return null;
        var newer = versions.SingleOrDefault(item => item.VersionNumber == toVersion);
        var older = versions.SingleOrDefault(item => item.VersionNumber == toVersion - 1);
        if (newer is null || older is null) return null;
        var before = older.Sections.ToDictionary(section => section.Key);
        var after = newer.Sections.ToDictionary(section => section.Key);
        var added = after.Values.SelectMany(section => section.Items.Except(before.GetValueOrDefault(section.Key)?.Items ?? [], StringComparer.OrdinalIgnoreCase)).Distinct().Take(25).ToArray();
        var removed = before.Values.SelectMany(section => section.Items.Except(after.GetValueOrDefault(section.Key)?.Items ?? [], StringComparer.OrdinalIgnoreCase)).Distinct().Take(25).ToArray();
        var changed = after.Values.Where(section => before.TryGetValue(section.Key, out var old) &&
            !section.Items.SequenceEqual(old.Items)).Select(section => section.Title).ToArray();
        var uncertainties = after.Values.Where(section => section.Title.Contains("uncertaint", StringComparison.OrdinalIgnoreCase) || section.Title == "Open questions")
            .SelectMany(section => section.Items).Except(before.Values.Where(section => section.Title.Contains("uncertaint", StringComparison.OrdinalIgnoreCase) || section.Title == "Open questions")
                .SelectMany(section => section.Items), StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        return new BriefingChangeResponse(older.VersionNumber, newer.VersionNumber, added, changed, removed, uncertainties);
    }

    private static IReadOnlyList<InvestigationResponse> NewerCandidates(ResearchBriefing brief, ResearchBriefingVersion version,
        IReadOnlyList<BriefingSourceSnapshot> sources, IReadOnlyList<InvestigationResponse> available)
    {
        var topics = BriefingTemplates.Topics[brief.Template].Concat(sources.SelectMany(source => source.Topics)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceById = sources.ToDictionary(source => source.InvestigationId);
        return available.Where(item => item.Status is InvestigationStatus.Ready or InvestigationStatus.Done &&
            item.MaterialUpdatedAt > version.ResearchThrough &&
            (brief.Template == "Custom" || item.Topics.Any(topics.Contains)) &&
            (!sourceById.TryGetValue(item.Id, out var previous) || item.MaterialUpdatedAt > previous.MaterialUpdatedAt))
            .OrderByDescending(item => item.MaterialUpdatedAt).ToArray();
    }

    private static BriefingSourceSnapshot[] SelectSources(IReadOnlyList<InvestigationResponse> available, IReadOnlyList<Guid> selectedIds)
    {
        if (selectedIds.Count is < 1 or > 12 || selectedIds.Distinct().Count() != selectedIds.Count)
            throw new ArgumentException("Select one to twelve distinct Investigations.");
        var byId = available.ToDictionary(item => item.Id);
        var selected = new List<BriefingSourceSnapshot>();
        foreach (var id in selectedIds)
        {
            if (!byId.TryGetValue(id, out var item) || item.Status is InvestigationStatus.Running or InvestigationStatus.Failed)
                throw new ArgumentException("A selected Investigation is unavailable or not ready.");
            selected.Add(new BriefingSourceSnapshot(item.Id, item.MaterialKind, item.MaterialId, item.Title,
                item.Origin, item.Purpose, item.Topics, item.MaterialUpdatedAt, Bound(item.Summary, 6_000),
                item.Claims.Take(30).ToArray(), item.SourceLeads.Take(30).ToArray(), item.Uncertainties.Take(30).ToArray(),
                Bound(item.RawResponse ?? item.RawMaterial, 8_000)));
        }
        return selected.ToArray();
    }

    private static void ValidateDefinition(string title, string template, string objective)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) throw new ArgumentException("Briefing title must be 1–200 characters.");
        if (!BriefingTemplates.Sections.ContainsKey(template)) throw new ArgumentException("Choose a supported Briefing template.");
        if (string.IsNullOrWhiteSpace(objective) || objective.Length > 2_000) throw new ArgumentException("Briefing objective must be 1–2,000 characters.");
    }

    private static ResearchBriefingVersion NewVersion(ResearchBriefing brief, int number, DateTimeOffset now,
        IReadOnlyList<BriefingSourceSnapshot> sources, IReadOnlyList<BriefingSection> sections,
        string? title = null, string? template = null, string? objective = null) => new()
        {
            BriefingId = brief.Id, VersionNumber = number, GeneratedAt = now,
            ResearchThrough = sources.Max(source => source.MaterialUpdatedAt),
            Title = title ?? brief.Title, Template = template ?? brief.Template, Objective = objective ?? brief.Objective,
            SectionsJson = JsonSerializer.Serialize(sections, JsonOptions),
            SourcesJson = JsonSerializer.Serialize(sources, JsonOptions)
        };

    private static BriefingResponse ToResponse(ResearchBriefing brief, ResearchBriefingVersion current, int versionCount, int newerCount) =>
        new(brief.Id, brief.CompanyId, brief.Title, brief.Template, brief.Objective, brief.CreatedAt, brief.UpdatedAt,
            brief.ArchivedAt, ToVersion(current), versionCount, newerCount);

    private static BriefingVersionResponse ToVersion(ResearchBriefingVersion item) =>
        new(item.Id, item.VersionNumber, item.GeneratedAt, item.ResearchThrough, item.Title, item.Template, item.Objective,
            JsonSerializer.Deserialize<BriefingSection[]>(item.SectionsJson, JsonOptions) ?? [], Sources(item));

    private static BriefingSourceSnapshot[] Sources(ResearchBriefingVersion item) =>
        JsonSerializer.Deserialize<BriefingSourceSnapshot[]>(item.SourcesJson, JsonOptions) ?? [];

    private static string Bound(string? value, int length) => string.IsNullOrWhiteSpace(value) ? "" : value.Length <= length ? value : value[..length];
}
