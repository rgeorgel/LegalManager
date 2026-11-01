using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcessosOabEscavador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessosOabEscavador",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroOab = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    NumeroCNJ = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessosOabEscavador", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessosOabEscavador_NumeroOab_Uf_NumeroCNJ",
                table: "ProcessosOabEscavador",
                columns: new[] { "NumeroOab", "Uf", "NumeroCNJ" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessosOabEscavador");
        }
    }
}
