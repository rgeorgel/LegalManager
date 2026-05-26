using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEscavadorMonitoramentoIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Processos_EscavadorMonitoramentoId",
                table: "Processos",
                column: "EscavadorMonitoramentoId",
                filter: "\"EscavadorMonitoramentoId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Processos_EscavadorMonitoramentoId",
                table: "Processos");
        }
    }
}
