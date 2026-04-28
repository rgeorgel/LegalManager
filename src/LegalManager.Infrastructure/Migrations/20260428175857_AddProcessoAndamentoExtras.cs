using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessoAndamentoExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CodigoClasse",
                table: "Processos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DataJulgamento",
                table: "Processos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DataPublicacao",
                table: "Processos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisaoDataJud",
                table: "Processos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ementa",
                table: "Processos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instancia",
                table: "Processos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observacao",
                table: "Processos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Relator",
                table: "Processos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultadoJulgamento",
                table: "Processos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoDecisao",
                table: "Processos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DadosExtras",
                table: "Andamentos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodigoClasse",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "DataJulgamento",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "DataPublicacao",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "DecisaoDataJud",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "Ementa",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "Instancia",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "Observacao",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "Relator",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "ResultadoJulgamento",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "TipoDecisao",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "DadosExtras",
                table: "Andamentos");
        }
    }
}
