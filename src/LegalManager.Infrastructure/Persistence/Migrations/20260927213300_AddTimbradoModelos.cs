using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimbradoModelos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Tenants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telefone",
                table: "Tenants",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimbradoComplemento",
                table: "Tenants",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UsarTimbrado",
                table: "ModelosDocumento",
                type: "boolean",
                nullable: false,
                defaultValue: true); // modelos existentes passam a usar o timbre (padrão)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Telefone",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TimbradoComplemento",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "UsarTimbrado",
                table: "ModelosDocumento");
        }
    }
}
