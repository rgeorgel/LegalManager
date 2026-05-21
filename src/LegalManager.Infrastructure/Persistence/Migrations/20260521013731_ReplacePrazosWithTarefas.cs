using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplacePrazosWithTarefas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Prazos");

            migrationBuilder.AddColumn<DateTime>(
                name: "DataInicio",
                table: "Tarefas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuantidadeDias",
                table: "Tarefas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoCalculo",
                table: "Tarefas",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataInicio",
                table: "Tarefas");

            migrationBuilder.DropColumn(
                name: "QuantidadeDias",
                table: "Tarefas");

            migrationBuilder.DropColumn(
                name: "TipoCalculo",
                table: "Tarefas");

            migrationBuilder.CreateTable(
                name: "Prazos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AndamentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponsavelId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataFinal = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Observacoes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    QuantidadeDias = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TipoCalculo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prazos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prazos_Andamentos_AndamentoId",
                        column: x => x.AndamentoId,
                        principalTable: "Andamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Prazos_AspNetUsers_ResponsavelId",
                        column: x => x.ResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Prazos_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Prazos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Prazos_AndamentoId",
                table: "Prazos",
                column: "AndamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Prazos_ProcessoId",
                table: "Prazos",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_Prazos_ResponsavelId",
                table: "Prazos",
                column: "ResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_Prazos_TenantId_DataFinal",
                table: "Prazos",
                columns: new[] { "TenantId", "DataFinal" });

            migrationBuilder.CreateIndex(
                name: "IX_Prazos_TenantId_Status",
                table: "Prazos",
                columns: new[] { "TenantId", "Status" });
        }
    }
}
