using Microsoft.Extensions.DependencyInjection;

namespace Raven.Api.Features.Chat;

public static class ChatServiceCollectionExtensions
{
    public static IServiceCollection AddCompanyChat(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ChatResearchOptions>()
            .Bind(configuration.GetSection(ChatResearchOptions.SectionName))
            .Validate(options => options.MaxResearchRounds is >= 1 and <= 3, "Chat research rounds must be between 1 and 3.")
            .Validate(options => options.MaxSearchCalls is >= 1 and <= 3, "Chat search calls must be between 1 and 3.")
            .Validate(options => options.MaxCrawlCalls is >= 1 and <= 5, "Chat crawl calls must be between 1 and 5.")
            .Validate(options => options.MaxResultsPerSearch is >= 1 and <= 10, "Chat search results must be between 1 and 10.")
            .Validate(options => options.MaxParallelCrawls >= 1 && options.MaxParallelCrawls <= options.MaxCrawlCalls,
                "Parallel chat crawls must be positive and cannot exceed the crawl budget.")
            .Validate(options => options.TurnDeadlineSeconds >= 60, "Chat deadline must be at least 60 seconds.")
            .Validate(options => options.FinalReserveSeconds > 0 && options.FinalReserveSeconds < options.TurnDeadlineSeconds,
                "The final-answer reserve must be positive and shorter than the turn deadline.")
            .Validate(options => options.PlannerTimeoutSeconds > 0 && options.FinalTimeoutSeconds > 0,
                "Chat model timeouts must be positive.")
            .Validate(options => options.MaxContextCharacters >= 16_000,
                "Chat context must allow at least 16,000 characters.")
            .ValidateOnStart();
        services.AddScoped<ChatEvidenceTool>();
        services.AddScoped<ChatWebSearchReranker>();
        services.AddScoped<ChatEvidenceChunker>();
        services.AddScoped<IChatActivityReporter, ChatActivityReporter>();
        services.AddScoped<ChatWebTool>(serviceProvider => new ChatWebTool(
            serviceProvider.GetRequiredService<Raven.Api.Features.Search.ISearchProvider>(),
            serviceProvider.GetRequiredService<Raven.Api.Features.Crawling.ICrawlerProvider>(),
            serviceProvider.GetRequiredService<ChatWebSearchReranker>(),
            serviceProvider.GetRequiredService<ChatEvidenceChunker>(),
            serviceProvider.GetRequiredService<Raven.Api.Features.Research.SourceUrlNormalizer>(),
            serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ChatResearchOptions>>(),
            serviceProvider.GetRequiredService<ILogger<ChatWebTool>>()));
        services.AddScoped<ICompanyChatAgentFactory, CompanyChatAgentFactory>();
        services.AddScoped<ICompanyChatService, CompanyChatService>();
        return services;
    }
}
