using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequerimentoInssEOrigem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProcessoOrigemId",
                table: "Processos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nit",
                table: "Contatos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RequerimentosInss",
                columns: table => new
                {
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Especie = table.Column<int>(type: "integer", nullable: true),
                    NumeroBeneficio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Unidade = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Situacao = table.Column<int>(type: "integer", nullable: false),
                    DataCiencia = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Dib = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Dcb = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Rmi = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    NumeroRecurso = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    InstanciaRecurso = table.Column<int>(type: "integer", nullable: true),
                    PericiaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PericiaLocal = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequerimentosInss", x => x.ProcessoId);
                    table.ForeignKey(
                        name: "FK_RequerimentosInss_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Processos_ProcessoOrigemId",
                table: "Processos",
                column: "ProcessoOrigemId");

            migrationBuilder.CreateIndex(
                name: "IX_RequerimentosInss_TenantId_Situacao",
                table: "RequerimentosInss",
                columns: new[] { "TenantId", "Situacao" });

            migrationBuilder.AddForeignKey(
                name: "FK_Processos_Processos_ProcessoOrigemId",
                table: "Processos",
                column: "ProcessoOrigemId",
                principalTable: "Processos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Processos_Processos_ProcessoOrigemId",
                table: "Processos");

            migrationBuilder.DropTable(
                name: "RequerimentosInss");

            migrationBuilder.DropIndex(
                name: "IX_Processos_ProcessoOrigemId",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "ProcessoOrigemId",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "Nit",
                table: "Contatos");
        }
    }
}
