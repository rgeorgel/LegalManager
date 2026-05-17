using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalAcessoToProcessoParte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PortalAcessoAtivo",
                table: "ProcessoPartes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PortalAcessoPendente",
                table: "ProcessoPartes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TokenConvite",
                table: "AcessosCliente",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TokenConviteExpiraEm",
                table: "AcessosCliente",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenRedefinicao",
                table: "AcessosCliente",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TokenRedefinicaoExpiraEm",
                table: "AcessosCliente",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PortalAcessoAtivo",
                table: "ProcessoPartes");

            migrationBuilder.DropColumn(
                name: "PortalAcessoPendente",
                table: "ProcessoPartes");

            migrationBuilder.DropColumn(
                name: "TokenConvite",
                table: "AcessosCliente");

            migrationBuilder.DropColumn(
                name: "TokenConviteExpiraEm",
                table: "AcessosCliente");

            migrationBuilder.DropColumn(
                name: "TokenRedefinicao",
                table: "AcessosCliente");

            migrationBuilder.DropColumn(
                name: "TokenRedefinicaoExpiraEm",
                table: "AcessosCliente");
        }
    }
}
