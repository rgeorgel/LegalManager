using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssistenteInteracoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InteracoesAssistente",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImpersonadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConversaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Pergunta = table.Column<string>(type: "text", nullable: false),
                    Resposta = table.Column<string>(type: "text", nullable: true),
                    Sucesso = table.Column<bool>(type: "boolean", nullable: false),
                    MensagemErro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FerramentasJson = table.Column<string>(type: "text", nullable: true),
                    QuantidadeFerramentas = table.Column<int>(type: "integer", nullable: false),
                    Provedor = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Modelo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TokensEntrada = table.Column<int>(type: "integer", nullable: false),
                    TokensSaida = table.Column<int>(type: "integer", nullable: false),
                    DuracaoMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteracoesAssistente", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InteracoesAssistente_ConversaId",
                table: "InteracoesAssistente",
                column: "ConversaId");

            migrationBuilder.CreateIndex(
                name: "IX_InteracoesAssistente_CriadoEm",
                table: "InteracoesAssistente",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_InteracoesAssistente_TenantId_CriadoEm",
                table: "InteracoesAssistente",
                columns: new[] { "TenantId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_InteracoesAssistente_UsuarioId",
                table: "InteracoesAssistente",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InteracoesAssistente");
        }
    }
}
