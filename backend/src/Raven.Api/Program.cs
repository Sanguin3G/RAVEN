using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Companies.Lifecycle;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Settings;
using Raven.Api.Features.Monitoring;
using Raven.Api.Features.DeepResearch;
using Raven.Api.Features.Research.SavedArtifacts;
using Microsoft.Extensions.AI;
using Raven.Api.Middleware;
using Raven.Api.Features.Profiles.Enrichment;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Companies.Workspace;
using Raven.Api.Features.Chat;
using Raven.Api.Features.ManagedResearch;
using Raven.Api.Features.Research.ExternalImport;
using Raven.Api.Features.Research.Organization;
using Raven.Api.Features.Research.Briefings;
using Raven.Api.Features.Speech;
using Raven.Api.Features.Auth;
using Raven.Api.Features.ProviderCredentials;
using Microsoft.Extensions.Logging.EventLog;

var builder = WebApplication.CreateBuilder(args);

// The Windows Event Log provider can throw when the local process does not
// have permission to create/write its source. Keep application logging on the
// providers supported by both local development and hosted environments.
if (OperatingSystem.IsWindows())
{
    builder.Logging.AddFilter<EventLogLoggerProvider>(_ => false);
}

builder.Services.AddDbContext<RavenDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Raven") ?? "Data Source=raven.db"));
builder.Services.AddHealthChecks().AddDbContextCheck<RavenDbContext>();
builder.Services.AddRavenIdentity(builder.Environment);
builder.Services.AddSingleton<IProviderCredentialVault, ProviderCredentialVault>();
builder.Services.AddScoped<IProviderCredentialResolver, ProviderCredentialResolver>();
builder.Services.AddScoped<ProviderCredentialConnectionTester>();
builder.Services.AddScoped<ProviderCredentialManagementService>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddExceptionHandler<ChatExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddPolicy("DevelopmentFrontend", policy =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5173"];

    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddCompanyFeatures();
builder.Services.AddScoped<ITargetedProfileUpdateService, TargetedProfileUpdateService>();
builder.Services.AddScoped<IResearchSettingsStore, EfResearchSettingsStore>();
builder.Services.AddScoped<IResearchSettingsService, ResearchSettingsService>();
builder.Services.AddSingleton<ResearchRunBackgroundQueue>();
builder.Services.AddSingleton<IResearchRunBackgroundQueue>(services => services.GetRequiredService<ResearchRunBackgroundQueue>());
builder.Services.AddHostedService(services => services.GetRequiredService<ResearchRunBackgroundQueue>());
builder.Services.AddSingleton<IMonitoringClock, SystemMonitoringClock>();
builder.Services.AddScoped<ICompanyMonitoringService, CompanyMonitoringService>();
builder.Services.AddScoped<ICompanyMonitoringStore, EfCompanyMonitoringStore>();
builder.Services.AddScoped<ICompanyMonitoringCoordinator, CompanyMonitoringCoordinator>();
builder.Services.AddHostedService<CompanyMonitoringWorker>();
builder.Services.AddSingleton<IDeepResearchQueue, DeepResearchQueue>();
builder.Services.AddHostedService<DeepResearchWorker>();
builder.Services.AddScoped<IDeepResearchRunStore, EfDeepResearchRunStore>();
builder.Services.AddScoped<EfDeepResearchActivityStore>();
builder.Services.AddScoped<IDeepResearchActivityStore>(services => services.GetRequiredService<EfDeepResearchActivityStore>());
builder.Services.AddScoped<IDeepResearchActivitySink>(services => services.GetRequiredService<EfDeepResearchActivityStore>());
builder.Services.AddScoped<IDeepResearchToolset, EfDeepResearchToolset>();
builder.Services.AddScoped<IChatClient, GeminiStructuredChatClient>();
builder.Services.AddScoped<IDeepResearchAgent, MafDeepResearchAgent>();
builder.Services.AddScoped<IDeepResearchRunService, DeepResearchRunService>();
builder.Services.AddScoped<ISavedResearchArtifactStore, EfSavedResearchArtifactStore>();
builder.Services.AddScoped<ISourceDocumentOwnershipReader, EfSourceDocumentOwnershipReader>();
builder.Services.AddSingleton<ISavedResearchArtifactClock, SystemSavedResearchArtifactClock>();
builder.Services.AddScoped<ISavedResearchArtifactService, SavedResearchArtifactService>();
builder.Services.AddScoped<InvestigationService>();
builder.Services.AddScoped<BriefingGenerator>();
builder.Services.AddScoped<BriefingService>();
builder.Services.AddSingleton<BriefingGenerationQueue>();
builder.Services.AddScoped<BriefingGenerationService>();
builder.Services.AddHostedService<BriefingGenerationWorker>();
builder.Services.AddHttpClient<GeminiSpeechService>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeminiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 30));
});
builder.Services.AddScoped<IInvestigationOrganizationService, InvestigationOrganizationService>();
builder.Services.AddScoped<IExternalResearchAnalysisJobStore, EfExternalResearchAnalysisJobStore>();
builder.Services.AddScoped<IExternalResearchAnalysisService, ExternalResearchAnalysisService>();
builder.Services.AddSingleton<ExternalResearchAnalysisQueue>();
builder.Services.AddSingleton<IExternalResearchAnalysisQueue>(services => services.GetRequiredService<ExternalResearchAnalysisQueue>());
builder.Services.AddHostedService<ExternalResearchAnalysisWorker>();
builder.Services.Configure<ExaAgentOptions>(builder.Configuration.GetSection(ExaAgentOptions.SectionName));
builder.Services.PostConfigure<ExaAgentOptions>(options => options.ApiKey ??= builder.Configuration[ExaAgentOptions.ApiKeyEnvironmentVariable]);
builder.Services.AddHttpClient<ExaAgentClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExaAgentOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
});
builder.Services.AddScoped<IManagedResearchAgentClient>(services => services.GetRequiredService<ExaAgentClient>());
builder.Services.AddScoped<IManagedResearchJobStore, EfManagedResearchJobStore>();
builder.Services.AddScoped<IManagedResearchInvestigationStore, EfManagedResearchInvestigationStore>();
builder.Services.AddScoped<IResearchContextAttachmentStore, EfResearchContextAttachmentStore>();
builder.Services.AddScoped<IResearchContextAttachmentService, ResearchContextAttachmentService>();
builder.Services.AddScoped<ManagedResearchChatBridge>();
builder.Services.AddScoped<IManagedResearchCompanyContextReader, EfManagedResearchCompanyContextReader>();
builder.Services.AddScoped<IManagedResearchBriefPreviewService, ManagedResearchBriefPreviewService>();
builder.Services.AddSingleton<IManagedResearchClock, SystemManagedResearchClock>();
builder.Services.AddSingleton<ManagedResearchJobQueue>();
builder.Services.AddSingleton<IManagedResearchJobQueue>(services => services.GetRequiredService<ManagedResearchJobQueue>());
builder.Services.AddScoped<IManagedResearchJobService, ManagedResearchJobService>();
builder.Services.AddHostedService<ManagedResearchWorker>();
builder.Services.Configure<Crawl4AiLocalOptions>(
    builder.Configuration.GetSection(Crawl4AiLocalOptions.SectionName));
builder.Services.PostConfigure<Crawl4AiLocalOptions>(options =>
{
    options.BaseUrl = builder.Configuration["CRAWL4AI_LOCAL_BASE_URL"] ?? options.BaseUrl;
    options.ApiToken = builder.Configuration["CRAWL4AI_API_TOKEN"] ?? options.ApiToken;
    options.CloudRunAudience = builder.Configuration["CRAWL4AI_CLOUD_RUN_AUDIENCE"] ?? options.CloudRunAudience;
});
builder.Services.AddHttpClient<ICrawlerStatusProbe, Crawl4AiLocalStatusProbe>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Crawl4AiLocalOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(string.IsNullOrWhiteSpace(options.CloudRunAudience) ? 3 : 45);
}).AddHttpMessageHandler<CloudRunCrawlerIdentityHandler>();
builder.Services.AddResearchDiscovery(builder.Configuration);
builder.Services.AddGeminiAi(builder.Configuration);
builder.Services.AddCompanyProfiles(builder.Configuration);
builder.Services.AddCompanyChat(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("DevelopmentFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseApiAntiforgery(builder.Configuration.GetValue("Security:ValidateApiAntiforgery", true));
app.MapHealthChecks("/health").AllowAnonymous();
// Keep the API health probe on the same /api surface used by the frontend
// dev proxy. The root route remains available for infrastructure probes.
app.MapHealthChecks("/api/health").AllowAnonymous();
app.MapGet("/api", () => Results.Ok(new { name = "RAVEN API", status = "initialized" }));
app.MapAuthEndpoints();
app.MapWorkspaceAccessEndpoints();
app.MapCompanyEndpoints();
app.MapCompanyLifecycleEndpoints();
app.MapCompanyWorkspaceEndpoints();
app.MapSystemEndpoints();
app.MapProviderCredentialEndpoints();
app.MapResearchSettingsEndpoints();
app.MapMonitoringEndpoints();
app.MapResearchEndpoints();
app.MapResearchCoverageEndpoints();
app.MapProfileEndpoints();
app.MapTargetedProfileUpdateEndpoints();
app.MapDeepResearchEndpoints();
app.MapChatEndpoints();
app.MapManagedResearchEndpoints();
app.MapExternalResearchEndpoints();
app.MapInvestigationOrganizationEndpoints();
app.MapInvestigationEndpoints();
app.MapBriefingEndpoints();
app.MapSpeechEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Environment.IsProduction())
{
    SqliteSeedDatabase.CopyIfMissing(
        builder.Configuration.GetConnectionString("Raven") ?? "Data Source=raven.db",
        Path.Combine(app.Environment.ContentRootPath, "seed", "raven.seed.db"));
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().InitializeAsync();
}

app.Run();

public partial class Program;
