using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguracaoPublicidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracoesPublicidade",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    PercentualProprios = table.Column<int>(type: "integer", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesPublicidade", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ConfiguracoesPublicidade",
                columns: new[] { "Id", "AtualizadoEm", "AtualizadoPorId", "PercentualProprios" },
                values: new object[] { 1, null, null, 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracoesPublicidade");
        }
    }
}
