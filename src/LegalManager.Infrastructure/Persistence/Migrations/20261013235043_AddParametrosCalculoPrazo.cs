using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParametrosCalculoPrazo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DataFinal",
                table: "CalculosPrazo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DataInicio",
                table: "CalculosPrazo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeriadosAdicionaisJson",
                table: "CalculosPrazo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuantidadeDias",
                table: "CalculosPrazo",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoCalculo",
                table: "CalculosPrazo",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataFinal",
                table: "CalculosPrazo");

            migrationBuilder.DropColumn(
                name: "DataInicio",
                table: "CalculosPrazo");

            migrationBuilder.DropColumn(
                name: "FeriadosAdicionaisJson",
                table: "CalculosPrazo");

            migrationBuilder.DropColumn(
                name: "QuantidadeDias",
                table: "CalculosPrazo");

            migrationBuilder.DropColumn(
                name: "TipoCalculo",
                table: "CalculosPrazo");
        }
    }
}
