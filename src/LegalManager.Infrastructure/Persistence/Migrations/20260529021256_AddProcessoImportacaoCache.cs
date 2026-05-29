using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessoImportacaoCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessosImportacaoCache",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroCNJ = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Fonte = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DadosJson = table.Column<string>(type: "text", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessosImportacaoCache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessosImportacaoCache_ExpiraEm",
                table: "ProcessosImportacaoCache",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessosImportacaoCache_TenantId_NumeroCNJ_Fonte",
                table: "ProcessosImportacaoCache",
                columns: new[] { "TenantId", "NumeroCNJ", "Fonte" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessosImportacaoCache");
        }
    }
}
