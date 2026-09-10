using System.Text.Json.Serialization;

namespace Raven.Api.Features.Profiles;

/// <summary>
/// The normalized company dossier fields shared by a generated candidate and an
/// accepted profile version. A missing scalar is intentionally represented by
/// <c>null</c>; unsupported collections remain empty.
/// </summary>
public abstract class CompanyProfileSnapshot
{
    public string? DisplayName { get; set; }
    public string? LegalName { get; set; }
    public string? Website { get; set; }
    public string? Country { get; set; }
    public string? Headquarters { get; set; }
    public string? RegistrationNumberOrTaxId { get; set; }
    public int? FoundedYear { get; set; }

    public string? PrimaryIndustry { get; set; }
    public ICollection<string> SecondaryIndustries { get; } = new List<string>();
    public string? CompanySize { get; set; }
    public int? EmployeeCount { get; set; }
    public string? EmployeeCountRange { get; set; }

    public string? Summary { get; set; }
    public ICollection<ProfileProductService> ProductsServices { get; } = new List<ProfileProductService>();
    public ICollection<ProfileMarket> Markets { get; } = new List<ProfileMarket>();
    public ICollection<ProfileLeader> Leadership { get; } = new List<ProfileLeader>();
    public ICollection<ProfileLocation> Locations { get; } = new List<ProfileLocation>();
    public ICollection<ProfilePublicLink> PublicLinks { get; } = new List<ProfilePublicLink>();
}

/// <summary>
/// A model-generated profile that is still awaiting human confirmation. It is
/// deliberately separate from <see cref="CompanyProfileVersion"/> so generation
/// can never overwrite an accepted dossier.
/// </summary>
public sealed class CompanyProfileCandidate : CompanyProfileSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public Guid ResearchRunId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? AiProvider { get; init; }
    public string? AiModel { get; init; }
    public string? PromptTemplateVersion { get; init; }
    public ICollection<ProfileEvidence> Evidence { get; } = new List<ProfileEvidence>();
    public ICollection<string> ValidationWarnings { get; } = new List<string>();

    /// <summary>Persistence representation for the reviewable generated payload.</summary>
    [JsonIgnore]
    public string CandidateJson { get; set; } = "{}";

    internal CompanyProfileCandidate Clone()
    {
        var clone = new CompanyProfileCandidate
        {
            Id = Id,
            CompanyId = CompanyId,
            ResearchRunId = ResearchRunId,
            GeneratedAt = GeneratedAt,
            AiProvider = AiProvider,
            AiModel = AiModel,
            PromptTemplateVersion = PromptTemplateVersion,
            DisplayName = DisplayName,
            LegalName = LegalName,
            Website = Website,
            Country = Country,
            Headquarters = Headquarters,
            RegistrationNumberOrTaxId = RegistrationNumberOrTaxId,
            FoundedYear = FoundedYear,
            PrimaryIndustry = PrimaryIndustry,
            CompanySize = CompanySize,
            EmployeeCount = EmployeeCount,
            EmployeeCountRange = EmployeeCountRange,
            Summary = Summary
        };

        foreach (var industry in SecondaryIndustries)
        {
            clone.SecondaryIndustries.Add(industry);
        }

        foreach (var product in ProductsServices)
        {
            clone.ProductsServices.Add(product with { });
        }

        foreach (var market in Markets)
        {
            clone.Markets.Add(market with { });
        }

        foreach (var leader in Leadership)
        {
            clone.Leadership.Add(leader with { });
        }

        foreach (var location in Locations)
        {
            clone.Locations.Add(location with { });
        }

        foreach (var link in PublicLinks)
        {
            clone.PublicLinks.Add(link with { });
        }

        foreach (var evidence in Evidence)
        {
            clone.Evidence.Add(evidence.Clone());
        }

        foreach (var warning in ValidationWarnings)
        {
            clone.ValidationWarnings.Add(warning);
        }

        return clone;
    }
}

/// <summary>
/// An immutable-in-product-lifecycle snapshot of a profile that a human has
/// confirmed. The persistence layer may use this entity as an append-only
/// version table.
/// </summary>
public sealed class CompanyProfileVersion : CompanyProfileSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public Guid ResearchRunId { get; init; }
    public int Version { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset ConfirmedAt { get; init; }
    public string? AiProvider { get; init; }
    public string? AiModel { get; init; }
    public string? PromptTemplateVersion { get; init; }
    public ICollection<ProfileEvidence> Evidence { get; } = new List<ProfileEvidence>();

    /// <summary>Persistence representation for the extensible profile payload.</summary>
    [JsonIgnore]
    public string ProfileJson { get; set; } = "{}";

    /// <summary>Creates an accepted version from a validated candidate.</summary>
    public static CompanyProfileVersion FromCandidate(CompanyProfileCandidate candidate, int version, DateTimeOffset? confirmedAt = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var accepted = new CompanyProfileVersion
        {
            CompanyId = candidate.CompanyId,
            ResearchRunId = candidate.ResearchRunId,
            Version = version,
            GeneratedAt = candidate.GeneratedAt,
            ConfirmedAt = confirmedAt ?? DateTimeOffset.UtcNow,
            AiProvider = candidate.AiProvider,
            AiModel = candidate.AiModel,
            PromptTemplateVersion = candidate.PromptTemplateVersion,
            DisplayName = candidate.DisplayName,
            LegalName = candidate.LegalName,
            Website = candidate.Website,
            Country = candidate.Country,
            Headquarters = candidate.Headquarters,
            RegistrationNumberOrTaxId = candidate.RegistrationNumberOrTaxId,
            FoundedYear = candidate.FoundedYear,
            PrimaryIndustry = candidate.PrimaryIndustry,
            CompanySize = candidate.CompanySize,
            EmployeeCount = candidate.EmployeeCount,
            EmployeeCountRange = candidate.EmployeeCountRange,
            Summary = candidate.Summary
        };

        foreach (var industry in candidate.SecondaryIndustries)
        {
            accepted.SecondaryIndustries.Add(industry);
        }

        foreach (var product in candidate.ProductsServices)
        {
            accepted.ProductsServices.Add(product with { });
        }

        foreach (var market in candidate.Markets)
        {
            accepted.Markets.Add(market with { });
        }

        foreach (var leader in candidate.Leadership)
        {
            accepted.Leadership.Add(leader with { });
        }

        foreach (var location in candidate.Locations)
        {
            accepted.Locations.Add(location with { });
        }

        foreach (var link in candidate.PublicLinks)
        {
            accepted.PublicLinks.Add(link with { });
        }

        foreach (var evidence in candidate.Evidence)
        {
            accepted.Evidence.Add(evidence.CloneForVersion(accepted.Id));
        }

        return accepted;
    }
}

public sealed record ProfileProductService(string Name, string? Type = null, string? Description = null);

public sealed record ProfileMarket(string Name, string? Type = null);

public sealed record ProfileLeader(string Name, string? Title = null);

public sealed record ProfileLocation(string? Name = null, string? Address = null, string? Country = null, string? Type = null);

public sealed record ProfilePublicLink(string Url, string? Kind = null, string? Label = null);

/// <summary>
/// Links one factual profile field to the source documents that support it.
/// A single record can carry multiple source IDs after duplicate normalization.
/// </summary>
public sealed class ProfileEvidence
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? CompanyProfileCandidateId { get; init; }
    public Guid? CompanyProfileVersionId { get; init; }
    public required string FieldPath { get; init; }
    public ICollection<Guid> SourceDocumentIds { get; } = new List<Guid>();

    /// <summary>Persistence representation for a bounded set of source IDs.</summary>
    [JsonIgnore]
    public string SourceDocumentIdsJson { get; set; } = "[]";

    internal ProfileEvidence Clone() => CloneForVersion(CompanyProfileVersionId);

    internal ProfileEvidence CloneForVersion(Guid? versionId)
    {
        var clone = new ProfileEvidence
        {
            Id = versionId is null ? Id : Guid.NewGuid(),
            CompanyProfileCandidateId = versionId is null ? CompanyProfileCandidateId : null,
            CompanyProfileVersionId = versionId,
            FieldPath = FieldPath
        };

        foreach (var sourceDocumentId in SourceDocumentIds)
        {
            clone.SourceDocumentIds.Add(sourceDocumentId);
        }

        return clone;
    }
}
