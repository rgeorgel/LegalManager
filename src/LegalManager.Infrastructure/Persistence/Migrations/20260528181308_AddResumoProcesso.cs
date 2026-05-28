using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResumoProcesso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResumosProcesso",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeradoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Conteudo = table.Column<string>(type: "text", nullable: false),
                    GeradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResumosProcesso", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResumosProcesso_AspNetUsers_GeradoPorId",
                        column: x => x.GeradoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResumosProcesso_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResumosProcesso_GeradoPorId",
                table: "ResumosProcesso",
                column: "GeradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_ResumosProcesso_ProcessoId",
                table: "ResumosProcesso",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_ResumosProcesso_TenantId",
                table: "ResumosProcesso",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ResumosProcesso_TenantId_ProcessoId",
                table: "ResumosProcesso",
                columns: new[] { "TenantId", "ProcessoId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResumosProcesso");
        }
    }
}
