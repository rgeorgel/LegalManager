using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResumoProcessoStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MensagemErro",
                table: "ResumosProcesso",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ResumosProcesso",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Resumos anteriores já estavam prontos (eram gerados na própria requisição).
                defaultValue: "Concluido");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MensagemErro",
                table: "ResumosProcesso");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ResumosProcesso");
        }
    }
}
