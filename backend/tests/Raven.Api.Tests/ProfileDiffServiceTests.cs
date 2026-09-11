using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Changes;

namespace Raven.Api.Tests;

public sealed class ProfileDiffServiceTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PreviousVersionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CurrentVersionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset DetectedAt = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compare_reports_changed_scalar_values_with_canonical_field_paths()
    {
        var previous = Version(PreviousVersionId, profile => profile.PrimaryIndustry = "Software");
        var current = Version(CurrentVersionId, profile => profile.PrimaryIndustry = "Telecommunications");

        var change = Assert.Single(new ProfileDiffService().Compare(previous, current, DetectedAt));

        Assert.Equal("primaryIndustry", change.FieldPath);
        Assert.Null(change.ItemKey);
        Assert.Equal(ProfileChangeType.Changed, change.ChangeType);
        Assert.Equal("\"Software\"", change.OldValueJson);
        Assert.Equal("\"Telecommunications\"", change.NewValueJson);
        Assert.Equal(PreviousVersionId, change.OldProfileVersionId);
        Assert.Equal(CurrentVersionId, change.NewProfileVersionId);
        Assert.Equal(DetectedAt, change.DetectedAt);
    }

    [Fact]
    public void Compare_reports_collection_add_remove_and_change_by_semantic_key()
    {
        var previous = Version(PreviousVersionId, profile =>
        {
            profile.ProductsServices.Add(new ProfileProductService("Platform", "Product", "Original description"));
            profile.Markets.Add(new ProfileMarket("Vietnam", "Country"));
            profile.Leadership.Add(new ProfileLeader("Alice Nguyen", "CEO"));
            profile.PublicLinks.Add(new ProfilePublicLink("https://example.com", "Website", "Website"));
        });
        var current = Version(CurrentVersionId, profile =>
        {
            profile.ProductsServices.Add(new ProfileProductService("Platform", "Product", "Updated description"));
            profile.ProductsServices.Add(new ProfileProductService("Research", "Service", "Research services"));
            profile.Leadership.Add(new ProfileLeader("Bob Tran", "CEO"));
        });

        var changes = new ProfileDiffService().Compare(previous, current, DetectedAt);

        var productChanged = Assert.Single(changes, change =>
            change.FieldPath == "productsServices" && change.ItemKey == "Platform");
        Assert.Equal(ProfileChangeType.Changed, productChanged.ChangeType);

        var productAdded = Assert.Single(changes, change =>
            change.FieldPath == "productsServices" && change.ItemKey == "Research");
        Assert.Equal(ProfileChangeType.Added, productAdded.ChangeType);
        Assert.Null(productAdded.OldValueJson);
        Assert.NotNull(productAdded.NewValueJson);

        var marketRemoved = Assert.Single(changes, change =>
            change.FieldPath == "markets" && change.ItemKey == "Vietnam");
        Assert.Equal(ProfileChangeType.Removed, marketRemoved.ChangeType);
        Assert.NotNull(marketRemoved.OldValueJson);
        Assert.Null(marketRemoved.NewValueJson);

        var leaderChanged = Assert.Single(changes, change =>
            change.FieldPath == "leadership" && change.ItemKey == "CEO");
        Assert.Equal(ProfileChangeType.Changed, leaderChanged.ChangeType);

        var linkRemoved = Assert.Single(changes, change =>
            change.FieldPath == "publicLinks" && change.ItemKey == "https://example.com");
        Assert.Equal(ProfileChangeType.Removed, linkRemoved.ChangeType);
    }

    [Fact]
    public void Compare_is_order_independent_when_semantic_collection_keys_are_unchanged()
    {
        var previous = Version(PreviousVersionId, profile =>
        {
            profile.ProductsServices.Add(new ProfileProductService("Platform", "Product", "Platform services"));
            profile.ProductsServices.Add(new ProfileProductService("Research", "Service", "Research services"));
            profile.Markets.Add(new ProfileMarket("Vietnam", "Country"));
            profile.Markets.Add(new ProfileMarket("Japan", "Country"));
            profile.Leadership.Add(new ProfileLeader("Alice Nguyen", "CEO"));
            profile.Leadership.Add(new ProfileLeader("Minh Le", "CTO"));
            profile.PublicLinks.Add(new ProfilePublicLink("https://example.com/", "Website", "Official website"));
        });
        var current = Version(CurrentVersionId, profile =>
        {
            profile.ProductsServices.Add(new ProfileProductService("Research", "Service", "Research services"));
            profile.ProductsServices.Add(new ProfileProductService("Platform", "Product", "Platform services"));
            profile.Markets.Add(new ProfileMarket("Japan", "Country"));
            profile.Markets.Add(new ProfileMarket("Vietnam", "Country"));
            profile.Leadership.Add(new ProfileLeader("Minh Le", "CTO"));
            profile.Leadership.Add(new ProfileLeader("Alice Nguyen", "CEO"));
            profile.PublicLinks.Add(new ProfilePublicLink("HTTPS://EXAMPLE.COM", "Website", "Official website"));
        });

        Assert.Empty(new ProfileDiffService().Compare(previous, current, DetectedAt));
    }

    [Fact]
    public void Compare_rejects_versions_from_different_companies()
    {
        var previous = Version(PreviousVersionId);
        var current = Version(CurrentVersionId, companyId: Guid.NewGuid());

        var exception = Assert.Throws<ArgumentException>(() =>
            new ProfileDiffService().Compare(previous, current, DetectedAt));

        Assert.Contains("same company", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CompanyProfileVersion Version(
        Guid id,
        Action<CompanyProfileVersion>? configure = null,
        Guid? companyId = null)
    {
        var version = new CompanyProfileVersion
        {
            Id = id,
            CompanyId = companyId ?? CompanyId,
            Version = 1,
            GeneratedAt = DetectedAt,
            ConfirmedAt = DetectedAt
        };
        configure?.Invoke(version);
        return version;
    }
}
