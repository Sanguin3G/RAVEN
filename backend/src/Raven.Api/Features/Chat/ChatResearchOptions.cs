namespace Raven.Api.Features.Chat;

public sealed class ChatResearchOptions
{
    public const string SectionName = "ChatResearch";

    public int MaxResearchRounds { get; set; } = 3;
    public int MaxSearchCalls { get; set; } = 3;
    public int MaxCrawlCalls { get; set; } = 5;
    public int MaxResultsPerSearch { get; set; } = 5;
    public int MaxParallelCrawls { get; set; } = 2;
    public int TurnDeadlineSeconds { get; set; } = 150;
    public int FinalReserveSeconds { get; set; } = 35;
    public int PlannerTimeoutSeconds { get; set; } = 30;
    public int FinalTimeoutSeconds { get; set; } = 45;
    public int MaxContextCharacters { get; set; } = 64_000;
}
