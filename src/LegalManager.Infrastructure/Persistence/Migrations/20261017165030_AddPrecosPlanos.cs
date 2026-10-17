using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrecosPlanos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrecosPlanos",
                columns: table => new
                {
                    Plano = table.Column<int>(type: "integer", nullable: false),
                    ValorMensal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrecosPlanos", x => x.Plano);
                });

            migrationBuilder.InsertData(
                table: "PrecosPlanos",
                columns: new[] { "Plano", "AtualizadoEm", "AtualizadoPorId", "ValorMensal" },
                values: new object[,]
                {
                    { 1, null, null, 50m },
                    { 3, null, null, 20m },
                    { 4, null, null, 125m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrecosPlanos");
        }
    }
}
