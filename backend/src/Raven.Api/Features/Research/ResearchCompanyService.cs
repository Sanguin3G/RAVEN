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

public sealed partial class ResearchCompanyService(
    RavenDbContext dbContext,
    ISearchProvider searchProvider,
    ICrawlerProvider crawlerProvider,
    SourceCandidateSelector candidateSelector,
    SourceUrlNormalizer urlNormalizer,
    IResearchEventWriter eventWriter,
    IResearchExecutionContext executionContext,
    ISourceSemanticReranker? sourceSemanticReranker = null,
    IResearchSettingsService? researchSettings = null,
    ICompanyProfilePersistenceService? profilePersistence = null,
    CoverageAwareSourceSelector? coverageAwareSourceSelector = null,
    TargetedQueryPlanner? targetedQueryPlanner = null,
    OfficialSiteEvidencePlanner? officialSiteEvidencePlanner = null,
    IResearchRunConfigurationSnapshot? configurationSnapshot = null,
    IResearchTelemetryFlusher? telemetryFlusher = null,
    ILogger<ResearchCompanyService>? logger = null,
    ResearchDiscoveryCoordinator? discoveryCoordinator = null,
    ResearchEvidenceAcquirer? evidenceAcquirer = null) : IResearchCompanyService
{
    private const int MaximumRecommendedCandidates = 5;
    private const int SearchResultsPerQuery = 5;

    private readonly IResearchRunConfigurationSnapshot? configurationSnapshot = configurationSnapshot;
    private readonly ResearchDiscoveryCoordinator sourceDiscovery = discoveryCoordinator ?? new(
        dbContext,
        searchProvider,
        candidateSelector,
        urlNormalizer,
        eventWriter,
        executionContext,
        sourceSemanticReranker,
        coverageAwareSourceSelector,
        targetedQueryPlanner,
        officialSiteEvidencePlanner);
    private readonly ResearchEvidenceAcquirer evidenceAcquirer = evidenceAcquirer ?? new(
        dbContext,
        crawlerProvider,
        urlNormalizer,
        eventWriter,
        executionContext);











}
