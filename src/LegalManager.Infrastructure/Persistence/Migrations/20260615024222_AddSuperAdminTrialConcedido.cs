using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuperAdminTrialConcedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TrialConcedidoDias",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialConcedidoEm",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrialConcedidoMotivo",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TrialConcedidoPorId",
                table: "Tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TrialConcedidoPorId",
                table: "Tenants",
                column: "TrialConcedidoPorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_AspNetUsers_TrialConcedidoPorId",
                table: "Tenants",
                column: "TrialConcedidoPorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_AspNetUsers_TrialConcedidoPorId",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_TrialConcedidoPorId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrialConcedidoDias",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrialConcedidoEm",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrialConcedidoMotivo",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrialConcedidoPorId",
                table: "Tenants");
        }
    }
}
