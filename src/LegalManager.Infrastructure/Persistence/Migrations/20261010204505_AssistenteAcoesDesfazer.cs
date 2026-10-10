using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssistenteAcoesDesfazer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DesfazerJson",
                table: "AcoesAssistente",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DesfeitaEm",
                table: "AcoesAssistente",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesfazerJson",
                table: "AcoesAssistente");

            migrationBuilder.DropColumn(
                name: "DesfeitaEm",
                table: "AcoesAssistente");
        }
    }
}
