namespace Raven.Api.Features.Search;

public sealed class BraveSearchOptions
{
    public const string SectionName = "Providers:Brave";

    public string BaseUrl { get; set; } = "https://api.search.brave.com";

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 15;
}
