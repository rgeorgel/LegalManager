using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeriadoTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_feriados_Data_Tipo_Uf_Municipio",
                table: "feriados");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "feriados",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_feriados_global_unique",
                table: "feriados",
                columns: new[] { "Data", "Tipo", "Uf", "Municipio" },
                unique: true,
                filter: "\"TenantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_feriados_tenant_unique",
                table: "feriados",
                columns: new[] { "Data", "TenantId", "Uf", "Municipio" },
                unique: true,
                filter: "\"TenantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_feriados_TenantId",
                table: "feriados",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_feriados_Tenants_TenantId",
                table: "feriados",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_feriados_Tenants_TenantId",
                table: "feriados");

            migrationBuilder.DropIndex(
                name: "idx_feriados_global_unique",
                table: "feriados");

            migrationBuilder.DropIndex(
                name: "idx_feriados_tenant_unique",
                table: "feriados");

            migrationBuilder.DropIndex(
                name: "IX_feriados_TenantId",
                table: "feriados");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "feriados");

            migrationBuilder.CreateIndex(
                name: "IX_feriados_Data_Tipo_Uf_Municipio",
                table: "feriados",
                columns: new[] { "Data", "Tipo", "Uf", "Municipio" },
                unique: true);
        }
    }
}
