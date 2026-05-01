using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHonorarioCalculo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HonorariosCalculos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Cliente = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Mode = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    Area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ItemOAB = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ValorCausa = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    HonorarioBase = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    HonorarioAjustado = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalCorrigido = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Multiplicador = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    TaxaCorrecao = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    FormaPagamento = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    Complexidade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Risco = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Urgencia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Capacidade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Exito = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StateJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HonorariosCalculos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HonorariosCalculos_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HonorariosCalculos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HonorariosCalculos_TenantId_UsuarioId_CriadoEm",
                table: "HonorariosCalculos",
                columns: new[] { "TenantId", "UsuarioId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_HonorariosCalculos_UsuarioId",
                table: "HonorariosCalculos",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HonorariosCalculos");
        }
    }
}
