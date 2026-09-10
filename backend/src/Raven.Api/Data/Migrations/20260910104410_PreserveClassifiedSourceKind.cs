using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreserveClassifiedSourceKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SourceKind",
                table: "SourceDocuments",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 32,
                oldDefaultValue: "ExternalWebsite");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SourceKind",
                table: "SourceDocuments",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "ExternalWebsite",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 32);
        }
    }
}
