using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodigosPromocionais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DescontoPromocionalUsadoEm",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CodigosPromocionais",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Plano = table.Column<int>(type: "integer", nullable: true),
                    DiasGratuitos = table.Column<int>(type: "integer", nullable: false),
                    DescontoPercentual = table.Column<int>(type: "integer", nullable: true),
                    DescontoMeses = table.Column<int>(type: "integer", nullable: true),
                    MaxUsos = table.Column<int>(type: "integer", nullable: true),
                    ValidoAte = table.Column<DateOnly>(type: "date", nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodigosPromocionais", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "CodigosPromocionais",
                columns: new[] { "Id", "Ativo", "AtualizadoEm", "Codigo", "CriadoEm", "CriadoPorId", "DescontoMeses", "DescontoPercentual", "Descricao", "DiasGratuitos", "MaxUsos", "Plano", "ValidoAte" },
                values: new object[] { new Guid("5f0c8a52-6d1e-4c1a-9b7e-2a4d3c1e0f01"), true, null, "primeiros20", new DateTime(2026, 9, 26, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "30 dias do plano Plus gratuito — aproveite!", 30, 20, 3, null });

            migrationBuilder.CreateIndex(
                name: "IX_CodigosPromocionais_Codigo",
                table: "CodigosPromocionais",
                column: "Codigo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodigosPromocionais");

            migrationBuilder.DropColumn(
                name: "DescontoPromocionalUsadoEm",
                table: "Tenants");
        }
    }
}
