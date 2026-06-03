using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropProcessoMonitorado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessosMonitoradosAndamentos");

            migrationBuilder.DropTable(
                name: "ProcessosMonitorados");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessosMonitorados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NomeExibicao = table.Column<string>(type: "text", nullable: true),
                    NumeroCNJ = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessosMonitorados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessosMonitorados_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessosMonitoradosAndamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoMonitoradoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodigoCNJ = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Data = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    Fonte = table.Column<int>(type: "integer", nullable: false),
                    OrgaoJulgador = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessosMonitoradosAndamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessosMonitoradosAndamentos_ProcessosMonitorados_Process~",
                        column: x => x.ProcessoMonitoradoId,
                        principalTable: "ProcessosMonitorados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessosMonitorados_TenantId",
                table: "ProcessosMonitorados",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessosMonitoradosAndamentos_ProcessoMonitoradoId",
                table: "ProcessosMonitoradosAndamentos",
                column: "ProcessoMonitoradoId");
        }
    }
}
