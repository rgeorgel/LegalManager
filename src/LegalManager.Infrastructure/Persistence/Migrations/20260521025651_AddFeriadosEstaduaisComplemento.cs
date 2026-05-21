using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeriadosEstaduaisComplemento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Feriados estaduais complementares — 2025 e 2026
            // RJ: Lei 5.198/2007 (São Sebastião) e Lei 2.920/1998 (São Jorge)
            // SE: Emancipação Política de Sergipe (8 de julho de 1820)
            // MG: Dia da Inconfidência Mineira (Lei Estadual 2.073/1961)
            // SC: Criação da Província de Santa Catarina (11/08/1822)
            // TODO: adicionar DF, ES, MT, PB quando as datas/leis forem confirmadas
            migrationBuilder.InsertData(
                table: "feriados",
                columns: ["Data", "Nome", "Tipo", "Uf", "Municipio", "Ativo"],
                values: new object[,]
                {
                    // ── 2025 ──────────────────────────────────────────────────────
                    { new DateOnly(2025, 1, 20), "São Sebastião",                           "estadual", "RJ", null, true },
                    { new DateOnly(2025, 4, 23), "São Jorge",                               "estadual", "RJ", null, true },
                    { new DateOnly(2025, 7, 8),  "Emancipação Política de Sergipe",         "estadual", "SE", null, true },
                    { new DateOnly(2025, 8, 11), "Criação da Província de Santa Catarina",  "estadual", "SC", null, true },
                    { new DateOnly(2025, 8, 24), "Dia da Inconfidência Mineira",            "estadual", "MG", null, true },
                    // ── 2026 ──────────────────────────────────────────────────────
                    { new DateOnly(2026, 1, 20), "São Sebastião",                           "estadual", "RJ", null, true },
                    { new DateOnly(2026, 4, 23), "São Jorge",                               "estadual", "RJ", null, true },
                    { new DateOnly(2026, 7, 8),  "Emancipação Política de Sergipe",         "estadual", "SE", null, true },
                    { new DateOnly(2026, 8, 11), "Criação da Província de Santa Catarina",  "estadual", "SC", null, true },
                    { new DateOnly(2026, 8, 24), "Dia da Inconfidência Mineira",            "estadual", "MG", null, true },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM feriados WHERE \"Tipo\" = 'estadual' AND \"Uf\" IN ('RJ', 'SE', 'MG', 'SC')");
        }
    }
}
