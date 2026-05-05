using System.Diagnostics.CodeAnalysis;
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    [ExcludeFromCodeCoverage]
    public partial class RenameNomeCapturaToProcessosMonitorados : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NomesCaptura");

            migrationBuilder.CreateTable(
                name: "ProcessosMonitorados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroCNJ = table.Column<string>(type: "text", nullable: false),
                    NomeExibicao = table.Column<string>(type: "text", nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_ProcessosMonitorados_TenantId",
                table: "ProcessosMonitorados",
                columns: new[] { "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessosMonitorados_TenantId_NumeroCNJ",
                table: "ProcessosMonitorados",
                columns: new[] { "TenantId", "NumeroCNJ" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessosMonitorados");

            migrationBuilder.CreateTable(
                name: "NomesCaptura",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NomesCaptura", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NomesCaptura_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NomesCaptura_TenantId_Ativo",
                table: "NomesCaptura",
                columns: new[] { "TenantId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_NomesCaptura_TenantId_Nome",
                table: "NomesCaptura",
                columns: new[] { "TenantId", "Nome" },
                unique: true);
        }
    }
}