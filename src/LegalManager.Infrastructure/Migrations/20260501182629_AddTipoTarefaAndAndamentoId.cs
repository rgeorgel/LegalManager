using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTipoTarefaAndAndamentoId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AndamentoId",
                table: "Tarefas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Tipo",
                table: "Tarefas",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AndamentoId",
                table: "Tarefas");

            migrationBuilder.DropColumn(
                name: "Tipo",
                table: "Tarefas");
        }
    }
}
