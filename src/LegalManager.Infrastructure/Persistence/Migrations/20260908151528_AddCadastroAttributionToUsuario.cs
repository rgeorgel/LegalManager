using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCadastroAttributionToUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fbclid",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gclid",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LandingPage",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemCadastro",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Referrer",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmCampaign",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmMedium",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmSource",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Fbclid",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Gclid",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LandingPage",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OrigemCadastro",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Referrer",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UtmCampaign",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UtmMedium",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UtmSource",
                table: "AspNetUsers");
        }
    }
}
