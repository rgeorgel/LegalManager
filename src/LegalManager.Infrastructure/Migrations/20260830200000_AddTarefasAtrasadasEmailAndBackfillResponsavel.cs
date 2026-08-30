using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Migrations
{
    [ExcludeFromCodeCoverage]
    public partial class AddTarefasAtrasadasEmailAndBackfillResponsavel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TarefasAtrasadasEmail",
                table: "PreferenciasNotificacoes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "TarefasAtrasadasInApp",
                table: "PreferenciasNotificacoes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql(@"
                UPDATE ""Tarefas""
                SET ""ResponsavelId"" = ""CriadoPorId""
                WHERE ""ResponsavelId"" IS NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TarefasAtrasadasInApp",
                table: "PreferenciasNotificacoes");

            migrationBuilder.DropColumn(
                name: "TarefasAtrasadasEmail",
                table: "PreferenciasNotificacoes");
        }
    }
}