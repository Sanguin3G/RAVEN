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
    /// <summary>
    /// A government-operated business or tax registry. This is intentionally
    /// separate from <see cref="BusinessDirectory"/>: the latter is a useful
    /// corroborating source but is not a government registry.
    /// </summary>
    OfficialBusinessRegistry,
    /// <summary>A third-party company or tax directory such as MaSoThue.</summary>
    BusinessDirectory,
    /// <summary>
    /// Legacy value retained so previously persisted source documents can still
    /// be read. New classification must use OfficialBusinessRegistry or
    /// BusinessDirectory.
    /// </summary>
    BusinessRegistry,
    TopCv,
    LinkedIn,
    News,
    ExternalWebsite,
    SearchResult
}
