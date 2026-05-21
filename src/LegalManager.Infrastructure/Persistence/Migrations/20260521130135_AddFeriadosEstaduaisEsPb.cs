using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeriadosEstaduaisEsPb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ES: Nossa Senhora da Penha — feriado estadual confirmado
            // PB: Fundação da Paraíba (05/ago) — data magna; João Pessoa (26/jul) — previsto em lei
            migrationBuilder.InsertData(
                table: "feriados",
                columns: ["Data", "Nome", "Tipo", "Uf", "Municipio", "Ativo"],
                values: new object[,]
                {
                    // ── 2025 ──────────────────────────────────────────────────────
                    { new DateOnly(2025, 4, 28), "Nossa Senhora da Penha",   "estadual", "ES", null, true },
                    { new DateOnly(2025, 7, 26), "João Pessoa",              "estadual", "PB", null, true },
                    { new DateOnly(2025, 8, 5),  "Fundação da Paraíba",      "estadual", "PB", null, true },
                    // ── 2026 ──────────────────────────────────────────────────────
                    { new DateOnly(2026, 4, 28), "Nossa Senhora da Penha",   "estadual", "ES", null, true },
                    { new DateOnly(2026, 7, 26), "João Pessoa",              "estadual", "PB", null, true },
                    { new DateOnly(2026, 8, 5),  "Fundação da Paraíba",      "estadual", "PB", null, true },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM feriados WHERE \"Tipo\" = 'estadual' AND \"Uf\" IN ('ES', 'PB')");
        }
    }
}
