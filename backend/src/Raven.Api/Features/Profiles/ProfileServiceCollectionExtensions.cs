using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Profiles.Generation;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Features.Profiles;

public static class ProfileServiceCollectionExtensions
{
    public static IServiceCollection AddCompanyProfiles(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProfileGenerationOptions>(configuration.GetSection(ProfileGenerationOptions.SectionName));
        services.AddScoped<IProfileInputBuilder>(serviceProvider => new ProfileInputBuilder(
            serviceProvider.GetRequiredService<ISourceAuthorityPolicy>()));
        services.AddScoped<IProfileGenerationService>(serviceProvider =>
        {
            var configured = serviceProvider.GetRequiredService<IOptions<ProfileGenerationOptions>>().Value;
            var runtimePreferences = serviceProvider.GetRequiredService<IRuntimeModelPreferences>().Current;
            var options = new ProfileGenerationOptions
            {
                // The Settings page owns the Fast Research selection for each new
                // profile-generation scope. Deployment configuration supplies the
                // initial value through RuntimeModelPreferences, rather than
                // silently overriding an explicit workspace selection here.
                Model = runtimePreferences.FastModel,
                PromptTemplateVersion = configured.PromptTemplateVersion
            };
            return new ProfileGenerationService(
                serviceProvider.GetRequiredService<IAiModelProvider>(),
                serviceProvider.GetRequiredService<IProfileInputBuilder>(),
                options);
        });
        services.AddScoped<ICompanyProfilePersistenceService, CompanyProfilePersistenceService>();
        services.AddScoped<ICompanyProfileWorkflowService, CompanyProfileWorkflowService>();
        return services;
    }
}
