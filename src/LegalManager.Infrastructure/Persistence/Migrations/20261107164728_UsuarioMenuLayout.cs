using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UsuarioMenuLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MenuLayout",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MenuLayout",
                table: "AspNetUsers");
        }
    }
}
