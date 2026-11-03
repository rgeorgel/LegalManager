using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnuncioProprioEstatisticas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnunciosPropriosEstatisticas",
                columns: table => new
                {
                    AnuncioProprioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dia = table.Column<DateOnly>(type: "date", nullable: false),
                    Impressoes = table.Column<int>(type: "integer", nullable: false),
                    Cliques = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnunciosPropriosEstatisticas", x => new { x.AnuncioProprioId, x.Dia });
                    table.ForeignKey(
                        name: "FK_AnunciosPropriosEstatisticas_AnunciosProprios_AnuncioPropri~",
                        column: x => x.AnuncioProprioId,
                        principalTable: "AnunciosProprios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnunciosPropriosEstatisticas");
        }
    }
}
