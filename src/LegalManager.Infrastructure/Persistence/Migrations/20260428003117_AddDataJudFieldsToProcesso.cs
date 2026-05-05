using System.Diagnostics.CodeAnalysis;
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    public partial class AddDataJudFieldsToProcesso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Assuntos",
                table: "Processos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Classe",
                table: "Processos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DataAjuizamento",
                table: "Processos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Formato",
                table: "Processos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Grau",
                table: "Processos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NivelSigilo",
                table: "Processos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sistema",
                table: "Processos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimaAtualizacaoDataJud",
                table: "Processos",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Assuntos",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "Classe",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "DataAjuizamento",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "Formato",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "Grau",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "NivelSigilo",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "Sistema",
                table: "Processos");

            migrationBuilder.DropColumn(
                name: "UltimaAtualizacaoDataJud",
                table: "Processos");
        }
    }
}
