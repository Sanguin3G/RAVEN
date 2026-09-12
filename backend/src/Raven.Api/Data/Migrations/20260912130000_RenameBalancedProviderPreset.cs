using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Raven.Api.Data.Migrations;

/// <summary>
/// Renames the persisted provider-fallback preset without resetting the
/// application-wide research settings row. ProviderPreset is stored as text,
/// so this is an explicit data migration rather than an enum ordinal change.
/// </summary>
[Migration("20260912130000_RenameBalancedProviderPreset")]
[DbContext(typeof(RavenDbContext))]
public partial class RenameBalancedProviderPreset : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "UPDATE ResearchSettings SET ProviderPreset = 'Resilient' WHERE ProviderPreset = 'Balanced';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "UPDATE ResearchSettings SET ProviderPreset = 'Balanced' WHERE ProviderPreset = 'Resilient';");
    }
}
