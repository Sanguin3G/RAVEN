using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyMonitoringSettings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Cadence = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NextRunAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastRunAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastRunStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ActiveClaimId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClaimExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyMonitoringSettings", x => x.CompanyId);
                    table.ForeignKey(
                        name: "FK_CompanyMonitoringSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMonitoringSettings_ClaimExpiresAt",
                table: "CompanyMonitoringSettings",
                column: "ClaimExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMonitoringSettings_Enabled_NextRunAt",
                table: "CompanyMonitoringSettings",
                columns: new[] { "Enabled", "NextRunAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyMonitoringSettings");
        }
    }
}
