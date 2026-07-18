using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImpersonacaoSuporte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImpersonadoPorId",
                table: "RefreshTokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImpersonadoPorId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ImpersonadoPorId",
                table: "RefreshTokens",
                column: "ImpersonadoPorId");

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_AspNetUsers_ImpersonadoPorId",
                table: "RefreshTokens",
                column: "ImpersonadoPorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_AspNetUsers_ImpersonadoPorId",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_ImpersonadoPorId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "ImpersonadoPorId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "ImpersonadoPorId",
                table: "AuditLogs");
        }
    }
}
