using Raven.Api.Features.Profiles;

namespace Raven.Api.Tests;

public sealed class ProfileValidationTests
{
    [Fact]
    public void Validation_preserves_unknown_scalars_as_null_and_unsupported_collections_as_empty()
    {
        var candidate = new CompanyProfileCandidate
        {
            CompanyId = CompanyId,
            ResearchRunId = RunId,
            DisplayName = "Example Co"
        };

        var result = CompanyProfileValidator.Validate(candidate, Context(SourceId));

        Assert.True(result.IsValid);
        Assert.Null(result.Candidate.LegalName);
        Assert.Null(result.Candidate.EmployeeCount);
        Assert.Empty(result.Candidate.SecondaryIndustries);
        Assert.Empty(result.Candidate.ProductsServices);
        Assert.Empty(result.Candidate.Evidence);
    }

    [Fact]
    public void Validation_drops_unknown_foreign_and_duplicate_source_references()
    {
        var unknownSource = Guid.NewGuid();
        var foreignCompanySource = Guid.NewGuid();
        var foreignRunSource = Guid.NewGuid();
        var candidate = new CompanyProfileCandidate
        {
            CompanyId = CompanyId,
            ResearchRunId = RunId
        };
        candidate.Evidence.Add(new ProfileEvidence
        {
            FieldPath = " LegalName ",
            SourceDocumentIds = { SourceId, SourceId, unknownSource, foreignCompanySource, foreignRunSource }
        });

        var context = new ProfileValidationContext(
            CompanyId,
            RunId,
            new HashSet<Guid> { SourceId, foreignRunSource },
            new HashSet<Guid> { SourceId, foreignCompanySource });

        var result = CompanyProfileValidator.Validate(candidate, context);

        Assert.True(result.IsValid);
        var evidence = Assert.Single(result.Candidate.Evidence);
        Assert.Equal("legalName", evidence.FieldPath);
        Assert.Equal([SourceId], evidence.SourceDocumentIds);
        Assert.Equal(3, result.Warnings.Count);
    }

    [Fact]
    public void Validation_merges_duplicate_field_paths_and_normalizes_case_and_indexes()
    {
        var candidate = new CompanyProfileCandidate
        {
            CompanyId = CompanyId,
            ResearchRunId = RunId
        };
        candidate.Evidence.Add(new ProfileEvidence
        {
            FieldPath = "Leadership[0]",
            SourceDocumentIds = { SourceId }
        });
        candidate.Evidence.Add(new ProfileEvidence
        {
            FieldPath = " leadership[0] ",
            SourceDocumentIds = { SecondSourceId, SourceId }
        });

        var result = CompanyProfileValidator.Validate(candidate, Context(SourceId, SecondSourceId));

        var evidence = Assert.Single(result.Candidate.Evidence);
        Assert.Equal("leadership[0]", evidence.FieldPath);
        Assert.Equal([SourceId, SecondSourceId], evidence.SourceDocumentIds);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("displayName", true)]
    [InlineData("identity.legalName", true)]
    [InlineData("secondaryIndustries[0]", true)]
    [InlineData("productsServices[0].description", true)]
    [InlineData("locations[12].address", true)]
    [InlineData("publicLinks[0].url", true)]
    [InlineData("company.secret", false)]
    [InlineData("leadership[0].sourceDocumentIds", false)]
    [InlineData("productsServices[].name", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Field_paths_are_limited_to_the_profile_schema(string fieldPath, bool expected)
    {
        Assert.Equal(expected, CompanyProfileValidator.IsPermittedFieldPath(fieldPath));
    }

    [Fact]
    public void Validation_rejects_candidate_for_a_different_company_or_run()
    {
        var candidate = new CompanyProfileCandidate
        {
            CompanyId = Guid.NewGuid(),
            ResearchRunId = RunId
        };
        candidate.Evidence.Add(new ProfileEvidence
        {
            FieldPath = "displayName",
            SourceDocumentIds = { SourceId }
        });

        var result = CompanyProfileValidator.Validate(candidate, Context(SourceId));

        Assert.False(result.IsValid);
        Assert.Empty(result.Candidate.Evidence);
        Assert.Contains(result.Warnings, warning => warning.Contains("company", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Confirming_a_candidate_creates_a_separate_version_and_copies_evidence()
    {
        var candidate = new CompanyProfileCandidate
        {
            CompanyId = CompanyId,
            ResearchRunId = RunId,
            DisplayName = "Example Co",
            GeneratedAt = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero)
        };
        candidate.ProductsServices.Add(new ProfileProductService("Research"));
        candidate.Evidence.Add(new ProfileEvidence
        {
            FieldPath = "displayName",
            SourceDocumentIds = { SourceId }
        });

        var version = CompanyProfileVersion.FromCandidate(candidate, 1, new DateTimeOffset(2026, 9, 10, 11, 0, 0, TimeSpan.Zero));

        Assert.NotEqual(candidate.Id, version.Id);
        Assert.Equal(candidate.DisplayName, version.DisplayName);
        Assert.Equal(1, version.Version);
        Assert.Equal(new DateTimeOffset(2026, 9, 10, 11, 0, 0, TimeSpan.Zero), version.ConfirmedAt);
        Assert.Equal(candidate.Evidence.Single().FieldPath, version.Evidence.Single().FieldPath);
        Assert.Equal(candidate.Evidence.Single().SourceDocumentIds, version.Evidence.Single().SourceDocumentIds);
        Assert.NotEqual(candidate.Evidence.Single().Id, version.Evidence.Single().Id);
    }

    private static ProfileValidationContext Context(params Guid[] sourceIds) =>
        new(CompanyId, RunId, new HashSet<Guid>(sourceIds), new HashSet<Guid>(sourceIds));

    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RunId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondSourceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
}
