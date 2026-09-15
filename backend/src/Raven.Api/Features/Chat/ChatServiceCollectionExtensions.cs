using Microsoft.Extensions.DependencyInjection;

namespace Raven.Api.Features.Chat;

public static class ChatServiceCollectionExtensions
{
    public static IServiceCollection AddCompanyChat(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ChatEvidenceTool>();
        services.AddScoped<ICompanyChatAgentFactory, CompanyChatAgentFactory>();
        services.AddScoped<ICompanyChatService, CompanyChatService>();
        return services;
    }
}
