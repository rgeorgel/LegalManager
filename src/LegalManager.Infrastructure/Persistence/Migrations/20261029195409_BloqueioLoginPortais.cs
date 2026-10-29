using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BloqueioLoginPortais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BloqueadoAte",
                table: "Parceiros",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TentativasLoginFalhas",
                table: "Parceiros",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "BloqueadoAte",
                table: "AcessosCliente",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TentativasLoginFalhas",
                table: "AcessosCliente",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // O login passou a contar senhas erradas (Identity Lockout). Usuário com
            // LockoutEnabled = false (criado fora do UserManager.CreateAsync) nunca bloquearia.
            migrationBuilder.Sql("""UPDATE "AspNetUsers" SET "LockoutEnabled" = TRUE WHERE "LockoutEnabled" = FALSE;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BloqueadoAte",
                table: "Parceiros");

            migrationBuilder.DropColumn(
                name: "TentativasLoginFalhas",
                table: "Parceiros");

            migrationBuilder.DropColumn(
                name: "BloqueadoAte",
                table: "AcessosCliente");

            migrationBuilder.DropColumn(
                name: "TentativasLoginFalhas",
                table: "AcessosCliente");
        }
    }
}
