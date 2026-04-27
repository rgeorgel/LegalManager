using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessoMonitoradoAndamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessosMonitoradosAndamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoMonitoradoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    CodigoCNJ = table.Column<int>(type: "integer", nullable: true),
                    OrgaoJulgador = table.Column<string>(type: "text", nullable: true),
                    Fonte = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                name: "IX_ProcessosMonitoradosAndamentos_ProcessoMonitoradoId",
                table: "ProcessosMonitoradosAndamentos",
                column: "ProcessoMonitoradoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessosMonitoradosAndamentos");
        }
    }
}
