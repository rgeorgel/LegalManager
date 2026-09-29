using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportacaoProcessos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportacoesProcessos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Modo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NumeroOab = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Processados = table.Column<int>(type: "integer", nullable: false),
                    Importados = table.Column<int>(type: "integer", nullable: false),
                    JaCadastrados = table.Column<int>(type: "integer", nullable: false),
                    Erros = table.Column<int>(type: "integer", nullable: false),
                    MensagemErro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcluidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportacoesProcessos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportacoesProcessos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportacaoProcessoItens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    NumeroCNJ = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Tribunal = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DadosJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Mensagem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportacaoProcessoItens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportacaoProcessoItens_ImportacoesProcessos_ImportacaoId",
                        column: x => x.ImportacaoId,
                        principalTable: "ImportacoesProcessos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportacaoProcessoItens_ImportacaoId_Ordem",
                table: "ImportacaoProcessoItens",
                columns: new[] { "ImportacaoId", "Ordem" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportacoesProcessos_TenantId_UsuarioId_Status",
                table: "ImportacoesProcessos",
                columns: new[] { "TenantId", "UsuarioId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportacaoProcessoItens");

            migrationBuilder.DropTable(
                name: "ImportacoesProcessos");
        }
    }
}
