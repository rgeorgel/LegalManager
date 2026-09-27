using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FiltroSalvoContatoMultiplo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ImportadoAutomaticamente",
                table: "ContatoFiltrosSalvos",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Tags",
                table: "ContatoFiltrosSalvos",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<int[]>(
                name: "TiposContato",
                table: "ContatoFiltrosSalvos",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            // Filtros salvos antes da seleção múltipla passam a ter uma lista de um item.
            migrationBuilder.Sql("""
                UPDATE "ContatoFiltrosSalvos" SET "TiposContato" = ARRAY["TipoContato"] WHERE "TipoContato" IS NOT NULL;
                UPDATE "ContatoFiltrosSalvos" SET "Tags" = ARRAY["Tag"]::text[] WHERE "Tag" IS NOT NULL AND "Tag" <> '';
                """);

            migrationBuilder.DropColumn(
                name: "Tag",
                table: "ContatoFiltrosSalvos");

            migrationBuilder.DropColumn(
                name: "TipoContato",
                table: "ContatoFiltrosSalvos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportadoAutomaticamente",
                table: "ContatoFiltrosSalvos");

            migrationBuilder.AddColumn<string>(
                name: "Tag",
                table: "ContatoFiltrosSalvos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoContato",
                table: "ContatoFiltrosSalvos",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "ContatoFiltrosSalvos" SET "TipoContato" = "TiposContato"[1], "Tag" = left("Tags"[1], 100);
                """);

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "ContatoFiltrosSalvos");

            migrationBuilder.DropColumn(
                name: "TiposContato",
                table: "ContatoFiltrosSalvos");
        }
    }
}
