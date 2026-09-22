using Raven.Api.Features.Companies.Workspace;

namespace Raven.Api.Features.Companies;

public static class CompanyServiceCollectionExtensions
{
    public static IServiceCollection AddCompanyFeatures(this IServiceCollection services)
    {
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<ICompanyLifecycleService, CompanyLifecycleService>();
        services.AddScoped<Lifecycle.CompanyDeletionService>();
        services.AddSingleton<ICompanyHealthEvaluator, CompanyHealthEvaluator>();
        services.AddSingleton<ICompanyDuplicateGroupingService, CompanyDuplicateGroupingService>();
        services.AddScoped<ICompanyWorkspaceReviewService, CompanyWorkspaceReviewService>();
        return services;
    }
}
