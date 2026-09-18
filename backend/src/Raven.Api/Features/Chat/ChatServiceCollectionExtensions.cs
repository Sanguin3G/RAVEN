using Microsoft.Extensions.DependencyInjection;

namespace Raven.Api.Features.Chat;

public static class ChatServiceCollectionExtensions
{
    public static IServiceCollection AddCompanyChat(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ChatEvidenceTool>();
        services.AddSingleton<ChatWebSearchPolicy>();
        services.AddScoped<ChatWebSearchReranker>();
        services.AddScoped<ChatEvidenceReranker>();
        services.AddScoped<IChatActivityReporter, ChatActivityReporter>();
        services.AddScoped<ChatWebTool>(serviceProvider => new ChatWebTool(
            serviceProvider.GetRequiredService<Raven.Api.Features.Search.ISearchProvider>(),
            serviceProvider.GetRequiredService<Raven.Api.Features.Crawling.ICrawlerProvider>(),
            serviceProvider.GetRequiredService<ChatWebSearchReranker>(),
            serviceProvider.GetRequiredService<ChatEvidenceReranker>(),
            serviceProvider.GetRequiredService<Raven.Api.Features.Research.SourceUrlNormalizer>()));
        services.AddScoped<ICompanyChatAgentFactory, CompanyChatAgentFactory>();
        services.AddScoped<ICompanyChatService, CompanyChatService>();
        return services;
    }
}
