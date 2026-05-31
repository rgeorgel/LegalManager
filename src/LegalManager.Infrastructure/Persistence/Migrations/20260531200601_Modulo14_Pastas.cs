using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Modulo14_Pastas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PastaId",
                table: "Documentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Pastas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ParentPastaId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntidadeTipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pastas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pastas_Pastas_ParentPastaId",
                        column: x => x.ParentPastaId,
                        principalTable: "Pastas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Pastas_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_PastaId",
                table: "Documentos",
                column: "PastaId");

            migrationBuilder.CreateIndex(
                name: "IX_Pastas_ParentPastaId",
                table: "Pastas",
                column: "ParentPastaId");

            migrationBuilder.CreateIndex(
                name: "IX_Pastas_TenantId_EntidadeTipo_ParentPastaId",
                table: "Pastas",
                columns: new[] { "TenantId", "EntidadeTipo", "ParentPastaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pastas_TenantId_EntidadeTipo_ParentPastaId_Nome",
                table: "Pastas",
                columns: new[] { "TenantId", "EntidadeTipo", "ParentPastaId", "Nome" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Documentos_Pastas_PastaId",
                table: "Documentos",
                column: "PastaId",
                principalTable: "Pastas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documentos_Pastas_PastaId",
                table: "Documentos");

            migrationBuilder.DropTable(
                name: "Pastas");

            migrationBuilder.DropIndex(
                name: "IX_Documentos_PastaId",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "PastaId",
                table: "Documentos");
        }
    }
}
