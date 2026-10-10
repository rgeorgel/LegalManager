using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssistenteAcoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcoesAssistente",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImpersonadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConversaId = table.Column<Guid>(type: "uuid", nullable: false),
                    InteracaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntidadeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Titulo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    AlteracoesJson = table.Column<string>(type: "text", nullable: false),
                    DadosJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResolvidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Erro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcoesAssistente", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcoesAssistente_InteracaoId",
                table: "AcoesAssistente",
                column: "InteracaoId");

            migrationBuilder.CreateIndex(
                name: "IX_AcoesAssistente_TenantId_CriadoEm",
                table: "AcoesAssistente",
                columns: new[] { "TenantId", "CriadoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcoesAssistente");
        }
    }
}
