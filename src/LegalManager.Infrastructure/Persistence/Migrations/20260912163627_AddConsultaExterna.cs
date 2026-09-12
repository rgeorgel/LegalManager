using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsultaExterna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsultasExternas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Api = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TipoConsulta = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Origem = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ParametrosJson = table.Column<string>(type: "text", nullable: true),
                    Sucesso = table.Column<bool>(type: "boolean", nullable: false),
                    MensagemErro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    QuantidadeResultados = table.Column<int>(type: "integer", nullable: true),
                    ResultadoResumoJson = table.Column<string>(type: "text", nullable: true),
                    DuracaoMs = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultasExternas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsultasExternas_Api",
                table: "ConsultasExternas",
                column: "Api");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultasExternas_TenantId_CriadoEm",
                table: "ConsultasExternas",
                columns: new[] { "TenantId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsultasExternas_UsuarioId",
                table: "ConsultasExternas",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsultasExternas");
        }
    }
}
