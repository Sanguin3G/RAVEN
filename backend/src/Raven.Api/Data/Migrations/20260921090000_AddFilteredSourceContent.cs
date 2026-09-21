using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raven.Api.Data.Migrations
{
    [DbContext(typeof(RavenDbContext))]
    [Migration("20260921090000_AddFilteredSourceContent")]
    public partial class AddFilteredSourceContent : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilteredContent",
                table: "SourceDocuments",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilteredContent",
                table: "SourceDocuments");
        }
    }
}
