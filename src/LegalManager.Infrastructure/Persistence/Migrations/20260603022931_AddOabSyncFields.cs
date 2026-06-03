using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOabSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Variacoes",
                table: "TenantOabs");

            migrationBuilder.AddColumn<long>(
                name: "EscavadorMonitoramentoId",
                table: "TenantOabs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncError",
                table: "TenantOabs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimoSyncEm",
                table: "TenantOabs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantOabs_TenantId_EscavadorMonitoramentoId",
                table: "TenantOabs",
                columns: new[] { "TenantId", "EscavadorMonitoramentoId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantOabs_TenantId_EscavadorMonitoramentoId",
                table: "TenantOabs");

            migrationBuilder.DropColumn(
                name: "EscavadorMonitoramentoId",
                table: "TenantOabs");

            migrationBuilder.DropColumn(
                name: "SyncError",
                table: "TenantOabs");

            migrationBuilder.DropColumn(
                name: "UltimoSyncEm",
                table: "TenantOabs");

            migrationBuilder.AddColumn<string>(
                name: "Variacoes",
                table: "TenantOabs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }
    }
}
