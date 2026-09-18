using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistExternalResearchMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimsJson",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                maxLength: 400000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagedResearchJobId",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Objective",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderMetadataJson",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                maxLength: 120000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceLeadsJson",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                maxLength: 200000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UncertaintiesJson",
                table: "SavedResearchArtifacts",
                type: "TEXT",
                maxLength: 200000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimsJson",
                table: "SavedResearchArtifacts");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "SavedResearchArtifacts");

            migrationBuilder.DropColumn(
                name: "ManagedResearchJobId",
                table: "SavedResearchArtifacts");

            migrationBuilder.DropColumn(
                name: "Objective",
                table: "SavedResearchArtifacts");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "SavedResearchArtifacts");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "SavedResearchArtifacts");

            migrationBuilder.DropColumn(
                name: "ProviderMetadataJson",
                table: "SavedResearchArtifacts");

            migrationBuilder.DropColumn(
                name: "SourceLeadsJson",
                table: "SavedResearchArtifacts");

            migrationBuilder.DropColumn(
                name: "UncertaintiesJson",
                table: "SavedResearchArtifacts");
        }
    }
}
