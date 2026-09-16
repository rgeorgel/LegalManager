using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerguntasEnquete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Perguntas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Texto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TipoResposta = table.Column<int>(type: "integer", nullable: false),
                    OpcoesJson = table.Column<string>(type: "text", nullable: true),
                    Publico = table.Column<int>(type: "integer", nullable: false),
                    Segmento = table.Column<int>(type: "integer", nullable: false),
                    PlanoAlvo = table.Column<int>(type: "integer", nullable: true),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Perguntas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RespostasPerguntas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerguntaId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RespondenteTipo = table.Column<int>(type: "integer", nullable: false),
                    RespondenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    RespostaTexto = table.Column<string>(type: "text", nullable: true),
                    OpcaoEscolhida = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RespondidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RespostasPerguntas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Perguntas_Ativa_Publico",
                table: "Perguntas",
                columns: new[] { "Ativa", "Publico" });

            migrationBuilder.CreateIndex(
                name: "IX_RespostasPerguntas_PerguntaId_RespondenteTipo_RespondenteId",
                table: "RespostasPerguntas",
                columns: new[] { "PerguntaId", "RespondenteTipo", "RespondenteId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Perguntas");

            migrationBuilder.DropTable(
                name: "RespostasPerguntas");
        }
    }
}
