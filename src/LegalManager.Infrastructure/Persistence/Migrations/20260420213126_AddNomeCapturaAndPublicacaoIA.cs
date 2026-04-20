using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNomeCapturaAndPublicacaoIA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClassificacaoIA",
                table: "Publicacoes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Urgente",
                table: "Publicacoes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "NomesCaptura",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NomesCaptura", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NomesCaptura_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NomesCaptura_TenantId_Ativo",
                table: "NomesCaptura",
                columns: new[] { "TenantId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_NomesCaptura_TenantId_Nome",
                table: "NomesCaptura",
                columns: new[] { "TenantId", "Nome" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NomesCaptura");

            migrationBuilder.DropColumn(
                name: "ClassificacaoIA",
                table: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "Urgente",
                table: "Publicacoes");
        }
    }
}
