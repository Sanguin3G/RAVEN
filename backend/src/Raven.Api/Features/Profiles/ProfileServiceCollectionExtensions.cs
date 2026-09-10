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
            var gemini = serviceProvider.GetRequiredService<IOptions<GeminiOptions>>().Value;
            var configured = serviceProvider.GetRequiredService<IOptions<ProfileGenerationOptions>>().Value;
            var options = new ProfileGenerationOptions
            {
                Model = string.IsNullOrWhiteSpace(configured.Model) ? gemini.FastModel : configured.Model,
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
