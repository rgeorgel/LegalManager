using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodigoCNJAndOrgaoJulgadorToAndamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CodigoCNJ",
                table: "Andamentos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrgaoJulgador",
                table: "Andamentos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodigoCNJ",
                table: "Andamentos");

            migrationBuilder.DropColumn(
                name: "OrgaoJulgador",
                table: "Andamentos");
        }
    }
}
