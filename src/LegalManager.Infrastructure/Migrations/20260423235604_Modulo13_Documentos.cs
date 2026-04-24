using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Modulo13_Documentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModeloId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedPorId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documentos_AspNetUsers_UploadedPorId",
                        column: x => x.UploadedPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documentos_Contatos_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Documentos_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Documentos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_ClienteId",
                table: "Documentos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_ProcessoId",
                table: "Documentos",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_TenantId_ClienteId",
                table: "Documentos",
                columns: new[] { "TenantId", "ClienteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_TenantId_ContratoId",
                table: "Documentos",
                columns: new[] { "TenantId", "ContratoId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_TenantId_ModeloId",
                table: "Documentos",
                columns: new[] { "TenantId", "ModeloId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_TenantId_ProcessoId",
                table: "Documentos",
                columns: new[] { "TenantId", "ProcessoId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_UploadedPorId",
                table: "Documentos",
                column: "UploadedPorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Documentos");
        }
    }
}
