using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGruposPergunta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GrupoId",
                table: "Perguntas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GruposPergunta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GruposPergunta", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GruposPerguntaMembros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrupoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdicionadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GruposPerguntaMembros", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GruposPerguntaMembros_GrupoId_UsuarioId",
                table: "GruposPerguntaMembros",
                columns: new[] { "GrupoId", "UsuarioId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GruposPergunta");

            migrationBuilder.DropTable(
                name: "GruposPerguntaMembros");

            migrationBuilder.DropColumn(
                name: "GrupoId",
                table: "Perguntas");
        }
    }
}
