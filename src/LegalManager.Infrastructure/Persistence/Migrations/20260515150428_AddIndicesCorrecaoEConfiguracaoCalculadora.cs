using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndicesCorrecaoEConfiguracaoCalculadora : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracoesCalculadora",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdicionalEspecialidade = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesCalculadora", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfiguracoesCalculadora_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndicesCorrecaoMonetaria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Ano = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(8,6)", nullable: false),
                    Fonte = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndicesCorrecaoMonetaria", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracoesCalculadora_TenantId",
                table: "ConfiguracoesCalculadora",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndicesCorrecaoMonetaria_Tipo_Ano_Mes",
                table: "IndicesCorrecaoMonetaria",
                columns: new[] { "Tipo", "Ano", "Mes" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracoesCalculadora");

            migrationBuilder.DropTable(
                name: "IndicesCorrecaoMonetaria");
        }
    }
}
