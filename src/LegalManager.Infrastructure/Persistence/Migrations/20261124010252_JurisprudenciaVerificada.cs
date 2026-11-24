using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JurisprudenciaVerificada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CitacoesJson",
                table: "PecasGeradas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrecedentesUsadosJson",
                table: "PecasGeradas",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FontesPrecedentes",
                columns: table => new
                {
                    Chave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ListaCompleta = table.Column<bool>(type: "boolean", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    ArquivosProcessadosJson = table.Column<string>(type: "text", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoErro = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FontesPrecedentes", x => x.Chave);
                });

            migrationBuilder.CreateTable(
                name: "Precedentes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Tribunal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Classe = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClasseBase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Relator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OrgaoJulgador = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DataJulgamento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DataPublicacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Texto = table.Column<string>(type: "text", nullable: false),
                    Situacao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelado = table.Column<bool>(type: "boolean", nullable: false),
                    UrlFonte = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Fonte = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternoId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ImportadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Precedentes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Precedentes_Fonte_ExternoId",
                table: "Precedentes",
                columns: new[] { "Fonte", "ExternoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Precedentes_Texto",
                table: "Precedentes",
                column: "Texto")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:TsVectorConfig", "portuguese");

            migrationBuilder.CreateIndex(
                name: "IX_Precedentes_Tribunal_Tipo_ClasseBase_Numero",
                table: "Precedentes",
                columns: new[] { "Tribunal", "Tipo", "ClasseBase", "Numero" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FontesPrecedentes");

            migrationBuilder.DropTable(
                name: "Precedentes");

            migrationBuilder.DropColumn(
                name: "CitacoesJson",
                table: "PecasGeradas");

            migrationBuilder.DropColumn(
                name: "PrecedentesUsadosJson",
                table: "PecasGeradas");
        }
    }
}
