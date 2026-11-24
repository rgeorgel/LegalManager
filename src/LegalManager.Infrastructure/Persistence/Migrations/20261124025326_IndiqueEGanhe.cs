using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndiqueEGanhe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantOrigemId",
                table: "Parceiros",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UsuarioId",
                table: "Parceiros",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parceiros_UsuarioId",
                table: "Parceiros",
                column: "UsuarioId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Parceiros_UsuarioId",
                table: "Parceiros");

            migrationBuilder.DropColumn(
                name: "TenantOrigemId",
                table: "Parceiros");

            migrationBuilder.DropColumn(
                name: "UsuarioId",
                table: "Parceiros");
        }
    }
}
