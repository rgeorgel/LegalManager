using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AndamentosPendentesEBuscaPaginada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AndamentosPendentes",
                table: "Processos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimaConsultaEscavadorEm",
                table: "Processos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BuscaEscavadorConcluida",
                table: "ImportacoesProcessos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BuscaTribunaisConcluida",
                table: "ImportacoesProcessos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CursorEscavador",
                table: "ImportacoesProcessos",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AndamentosPendentes",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "UltimaConsultaEscavadorEm",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "BuscaEscavadorConcluida",
                table: "ImportacoesProcessos");

            migrationBuilder.DropColumn(
                name: "BuscaTribunaisConcluida",
                table: "ImportacoesProcessos");

            migrationBuilder.DropColumn(
                name: "CursorEscavador",
                table: "ImportacoesProcessos");
        }
    }
}
