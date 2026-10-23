using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TriagemPublicacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PrazoProcessual",
                table: "CalculosPrazo",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TriagensPublicacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    Erro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resumo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Ato = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PrazoDias = table.Column<int>(type: "integer", nullable: true),
                    TipoCalculo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PrazoTrecho = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AudienciaDataHora = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AudienciaTrecho = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DataPublicacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Vencimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MemoriaCalculo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TarefaId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvidaPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriagensPublicacao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TriagensPublicacao_Publicacoes_PublicacaoId",
                        column: x => x.PublicacaoId,
                        principalTable: "Publicacoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TriagensPublicacao_PublicacaoId",
                table: "TriagensPublicacao",
                column: "PublicacaoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TriagensPublicacao_Status_CriadoEm",
                table: "TriagensPublicacao",
                columns: new[] { "Status", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_TriagensPublicacao_TenantId_Status",
                table: "TriagensPublicacao",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TriagensPublicacao");

            migrationBuilder.DropColumn(
                name: "PrazoProcessual",
                table: "CalculosPrazo");
        }
    }
}
