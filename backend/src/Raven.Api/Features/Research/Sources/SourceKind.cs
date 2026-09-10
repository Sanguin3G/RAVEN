namespace Raven.Api.Features.Research.Sources;

/// <summary>
/// A neutral classification for a public source. Provider implementations should
/// map their results to this taxonomy instead of leaking provider-specific types
/// into the research domain.
/// </summary>
public enum SourceKind
{
    OfficialWebsite,
    OfficialDocument,
    BusinessRegistry,
    TopCv,
    LinkedIn,
    News,
    ExternalWebsite,
    SearchResult
}
