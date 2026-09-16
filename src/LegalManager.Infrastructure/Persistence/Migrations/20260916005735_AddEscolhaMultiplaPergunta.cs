using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEscolhaMultiplaPergunta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OpcoesEscolhidasJson",
                table: "RespostasPerguntas",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpcoesEscolhidasJson",
                table: "RespostasPerguntas");
        }
    }
}
