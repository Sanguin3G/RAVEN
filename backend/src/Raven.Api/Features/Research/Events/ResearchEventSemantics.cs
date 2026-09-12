namespace Raven.Api.Features.Research.Events;

/// <summary>
/// Maps the historical activity vocabulary onto the compact execution
/// vocabulary. The mapping is deliberately kept at the telemetry boundary so
/// old rows remain readable while new callers can use Category/Operation/Status.
/// </summary>
public static class ResearchEventSemantics
{
    public const int MaxOperationCharacters = 100;

    public static string NormalizeOperation(string? operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            return ResearchEvent.LegacyOperation;
        }

        var normalized = operation.Trim();
        return normalized.Length <= MaxOperationCharacters
            ? normalized
            : normalized[..MaxOperationCharacters];
    }

    public static ResearchEventCategory CanonicalCategory(ResearchEventCategory category) => category switch
    {
        ResearchEventCategory.SearchRequested or ResearchEventCategory.SearchCompleted => ResearchEventCategory.Search,
        ResearchEventCategory.CrawlRequested or ResearchEventCategory.CrawlCompleted or ResearchEventCategory.CrawlFailed => ResearchEventCategory.Crawl,
        ResearchEventCategory.AiRequested or ResearchEventCategory.AiCompleted or ResearchEventCategory.AiFailed => ResearchEventCategory.AI,
        ResearchEventCategory.GroundingRequested or ResearchEventCategory.GroundingCompleted or ResearchEventCategory.GroundingFailed or ResearchEventCategory.IdentitySelected => ResearchEventCategory.Identity,
        ResearchEventCategory.SourceSemanticRerankStarted or ResearchEventCategory.SourceSemanticRerankCompleted or ResearchEventCategory.CandidateDiscovery or ResearchEventCategory.CandidateRanking or ResearchEventCategory.OfficialDomainDiscovery or ResearchEventCategory.DuplicateSkipped => ResearchEventCategory.Research,
        ResearchEventCategory.ProviderFallback => ResearchEventCategory.Research,
        ResearchEventCategory.StructuredSourceParsed => ResearchEventCategory.Parsing,
        ResearchEventCategory.SourcePersisted => ResearchEventCategory.Research,
        ResearchEventCategory.ProfileValidated or ResearchEventCategory.ProfileConfirmed => ResearchEventCategory.Profile,
        ResearchEventCategory.MonitoringRunStarted or ResearchEventCategory.MonitoringUpdateReady => ResearchEventCategory.Monitoring,
        _ => category
    };

    public static string OperationFor(ResearchEvent researchEvent)
    {
        ArgumentNullException.ThrowIfNull(researchEvent);
        return OperationFor(researchEvent.Category, researchEvent.Operation);
    }

    public static string OperationFor(ResearchEventCategory category, string? operation)
    {
        var normalized = NormalizeOperation(operation);
        return normalized != ResearchEvent.LegacyOperation
            ? normalized
            : category switch
            {
                ResearchEventCategory.Search or ResearchEventCategory.SearchRequested or ResearchEventCategory.SearchCompleted => "web_search",
                ResearchEventCategory.Crawl or ResearchEventCategory.CrawlRequested or ResearchEventCategory.CrawlCompleted or ResearchEventCategory.CrawlFailed => "page_crawl",
                ResearchEventCategory.AI or ResearchEventCategory.AiRequested or ResearchEventCategory.AiCompleted or ResearchEventCategory.AiFailed => "profile_generation",
                ResearchEventCategory.GroundingRequested or ResearchEventCategory.GroundingCompleted or ResearchEventCategory.GroundingFailed => "identity_resolution",
                ResearchEventCategory.IdentitySelected => "identity_selection",
                ResearchEventCategory.SourceSemanticRerankStarted or ResearchEventCategory.SourceSemanticRerankCompleted => "source_relevance",
                ResearchEventCategory.ProviderFallback => "provider_fallback",
                ResearchEventCategory.StructuredSourceParsed => "source_parsing",
                ResearchEventCategory.SourcePersisted => "source_persistence",
                ResearchEventCategory.ProfileValidated => "profile_validation",
                ResearchEventCategory.ProfileConfirmed => "profile_confirmation",
                ResearchEventCategory.MonitoringRunStarted or ResearchEventCategory.MonitoringUpdateReady => "monitoring",
                _ => "research_activity"
            };
    }

    public static bool IsLogicalCallStart(ResearchEvent researchEvent)
    {
        ArgumentNullException.ThrowIfNull(researchEvent);
        if (NormalizeOperation(researchEvent.Operation) != ResearchEvent.LegacyOperation)
        {
            return CanonicalCategory(researchEvent.Category) is ResearchEventCategory.Search or ResearchEventCategory.Crawl or ResearchEventCategory.AI;
        }

        return researchEvent.Category is
            ResearchEventCategory.Search or ResearchEventCategory.SearchRequested or
            ResearchEventCategory.Crawl or ResearchEventCategory.CrawlRequested or
            ResearchEventCategory.AI or ResearchEventCategory.AiRequested;
    }

    public static bool IsLegacyOutcome(ResearchEvent researchEvent) =>
        researchEvent.Operation == ResearchEvent.LegacyOperation && researchEvent.Category is
            ResearchEventCategory.SearchCompleted or ResearchEventCategory.CrawlCompleted or ResearchEventCategory.CrawlFailed or
            ResearchEventCategory.AiCompleted or ResearchEventCategory.AiFailed;
}
