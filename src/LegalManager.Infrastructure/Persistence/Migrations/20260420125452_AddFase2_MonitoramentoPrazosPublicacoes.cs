using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFase2_MonitoramentoPrazosPublicacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UltimoMonitoramento",
                table: "Processos",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Prazos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    AndamentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DataInicio = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    QuantidadeDias = table.Column<int>(type: "integer", nullable: false),
                    TipoCalculo = table.Column<int>(type: "integer", nullable: false),
                    DataFinal = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResponsavelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Observacoes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "Publicacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    NumeroCNJ = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Diario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DataPublicacao = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Conteudo = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CapturaEm = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Publicacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Publicacoes_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Publicacoes_Tenants_TenantId",
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

            migrationBuilder.CreateIndex(
                name: "IX_Publicacoes_ProcessoId",
                table: "Publicacoes",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_Publicacoes_TenantId_DataPublicacao",
                table: "Publicacoes",
                columns: new[] { "TenantId", "DataPublicacao" });

            migrationBuilder.CreateIndex(
                name: "IX_Publicacoes_TenantId_Status",
                table: "Publicacoes",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Prazos");

            migrationBuilder.DropTable(
                name: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "UltimoMonitoramento",
                table: "Processos");
        }
    }
}
