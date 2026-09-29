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
}
