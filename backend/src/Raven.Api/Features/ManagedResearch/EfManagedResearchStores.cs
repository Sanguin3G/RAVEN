using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.ManagedResearch;

public sealed class EfManagedResearchJobStore(RavenDbContext db) : IManagedResearchJobStore
{
    public async Task AddAsync(ManagedResearchJob job, CancellationToken ct = default) { db.ManagedResearchJobs.Add(job); await db.SaveChangesAsync(ct); }
    public Task<ManagedResearchJob?> GetAsync(Guid id, CancellationToken ct = default) => db.ManagedResearchJobs.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<ManagedResearchJob?> GetAsync(Guid companyId, Guid id, CancellationToken ct = default) => db.ManagedResearchJobs.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct);
    public async Task UpdateAsync(ManagedResearchJob job, CancellationToken ct = default) { db.ManagedResearchJobs.Update(job); await db.SaveChangesAsync(ct); }
    public async Task<IReadOnlyList<ManagedResearchJob>> ListActiveAsync(CancellationToken ct = default) =>
        (await db.ManagedResearchJobs
            .Where(x => x.Status == ManagedResearchJobStatus.Queued || x.Status == ManagedResearchJobStatus.Researching)
            .ToListAsync(ct))
        .OrderBy(x => x.CreatedAt)
        .ToArray();
    public async Task<IReadOnlyList<ManagedResearchJob>> ListForCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        (await db.ManagedResearchJobs
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(ct))
        .OrderByDescending(x => x.CreatedAt)
        .ToArray();
}

public sealed class EfManagedResearchInvestigationStore(RavenDbContext db) : IManagedResearchInvestigationStore
{
    public async Task SaveAsync(ManagedResearchInvestigation item, CancellationToken ct = default) { db.ManagedResearchInvestigations.Add(item); await db.SaveChangesAsync(ct); }
    public Task<ManagedResearchInvestigation?> GetAsync(Guid companyId, Guid id, CancellationToken ct = default) => db.ManagedResearchInvestigations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct);
    public async Task<IReadOnlyList<ManagedResearchInvestigation>> ListForCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        (await db.ManagedResearchInvestigations
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(ct))
        .OrderByDescending(x => x.CompletedAt)
        .ToArray();
}

public sealed class EfManagedResearchCompanyContextReader(RavenDbContext db) : IManagedResearchCompanyContextReader
{
    public async Task<ManagedResearchCompanyContext?> GetAsync(Guid companyId, CancellationToken ct = default)
    {
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId, ct);
        if (company is null) return null;
        var profile = await db.CompanyProfileVersions.AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        string? summary = null;
        if (!string.IsNullOrWhiteSpace(profile?.ProfileJson))
        {
            try { using var json = JsonDocument.Parse(profile.ProfileJson); summary = json.RootElement.TryGetProperty("summary", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null; } catch (JsonException) { }
        }
        return new ManagedResearchCompanyContext { CompanyId = company.Id, DisplayName = company.Name, LegalName = company.LegalName, OfficialWebsite = company.Website, Country = company.Country, Headquarters = company.Headquarters, AcceptedProfileSummary = summary };
    }
}
