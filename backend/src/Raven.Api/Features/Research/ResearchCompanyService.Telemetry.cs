using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research.Parsing;
using Raven.Api.Features.Research.Planning;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Identity;
using Raven.Api.Features.Search;
using Raven.Api.Features.Settings;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research.Routing;
using Raven.Api.Features.Research.Coverage;

namespace Raven.Api.Features.Research;

public sealed partial class ResearchCompanyService
{
    private async Task<ResearchRunResponse> FailAsync(
        ResearchRun run,
        string error,
        CancellationToken cancellationToken)
    {
        run.Status = ResearchRunStatus.Failed;
        run.Stage = ResearchStage.Failed;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Error = error[..Math.Min(error.Length, 4_000)];
        await dbContext.SaveChangesAsync(cancellationToken);
        await FlushTelemetryAsync(cancellationToken);
        return ToResponse(run);
    }

    private Task WriteEventAsync(
        ResearchRun run,
        ResearchEventCategory category,
        ResearchEventStatus status,
        string? provider,
        string? outputSummary,
        CancellationToken cancellationToken) =>
        eventWriter.WriteAsync(new ResearchEvent
        {
            ResearchRunId = run.Id,
            Stage = run.Stage,
            Category = category,
            Status = status,
            Provider = provider,
            OutputSummary = outputSummary
        }, cancellationToken);

    private async Task FlushTelemetryAsync(CancellationToken cancellationToken)
    {
        if (telemetryFlusher is null) return;
        try
        {
            await telemetryFlusher.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(exception, "Could not flush research execution telemetry; the run remains successful.");
        }
    }
}
