namespace Raven.DemoSeed;

public sealed record DemoSeedManifest(IReadOnlyList<DemoCompanySelection> Companies);

public sealed record DemoCompanySelection(
    Guid Id,
    string Role,
    IReadOnlyList<Guid>? ExcludeChatConversationIds = null,
    IReadOnlyList<Guid>? ExcludeInvestigationIds = null,
    IReadOnlyList<Guid>? ExcludeBriefingIds = null);

public sealed record DemoSeedExportOptions(
    string SourcePath,
    string ManifestPath,
    string OutputPath,
    string? PublicSeedPath = null);
