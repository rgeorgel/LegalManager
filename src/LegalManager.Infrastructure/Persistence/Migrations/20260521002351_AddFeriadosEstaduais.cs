using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeriadosEstaduais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Feriados estaduais — 2025 e 2026
            // Fonte: legislação estadual vigente. Verificar atualizações em anos futuros.
            migrationBuilder.InsertData(
                table: "feriados",
                columns: ["Data", "Nome", "Tipo", "Uf", "Municipio", "Ativo"],
                values: new object[,]
                {
                    // ── 2025 ──────────────────────────────────────────────────────
                    { new DateOnly(2025, 1, 4),  "Criação do Estado de Rondônia",                        "estadual", "RO", null, true },
                    { new DateOnly(2025, 3, 6),  "Revolução Pernambucana",                               "estadual", "PE", null, true },
                    { new DateOnly(2025, 3, 25), "Data Magna do Ceará",                                  "estadual", "CE", null, true },
                    { new DateOnly(2025, 6, 15), "Aniversário do Estado do Acre",                        "estadual", "AC", null, true },
                    { new DateOnly(2025, 7, 2),  "Independência da Bahia",                               "estadual", "BA", null, true },
                    { new DateOnly(2025, 7, 9),  "Revolução Constitucionalista de 1932",                 "estadual", "SP", null, true },
                    { new DateOnly(2025, 7, 26), "Fundação da Cidade de Goiás",                          "estadual", "GO", null, true },
                    { new DateOnly(2025, 7, 28), "Adesão do Maranhão à Independência do Brasil",         "estadual", "MA", null, true },
                    { new DateOnly(2025, 8, 15), "Adesão do Grão-Pará à Independência do Brasil",        "estadual", "PA", null, true },
                    { new DateOnly(2025, 9, 5),  "Elevação à Categoria de Estado do Amazonas",           "estadual", "AM", null, true },
                    { new DateOnly(2025, 9, 16), "Emancipação Política de Alagoas",                      "estadual", "AL", null, true },
                    { new DateOnly(2025, 9, 20), "Proclamação da República Rio-Grandense",               "estadual", "RS", null, true },
                    { new DateOnly(2025, 10, 3), "Mártires de Cunhaú e Uruaçu",                         "estadual", "RN", null, true },
                    { new DateOnly(2025, 10, 5), "Criação do Estado do Amapá",                           "estadual", "AP", null, true },
                    { new DateOnly(2025, 10, 5), "Criação do Estado de Roraima",                         "estadual", "RR", null, true },
                    { new DateOnly(2025, 10, 5), "Criação do Estado do Tocantins",                       "estadual", "TO", null, true },
                    { new DateOnly(2025, 10, 11),"Criação do Estado de Mato Grosso do Sul",              "estadual", "MS", null, true },
                    { new DateOnly(2025, 10, 19),"Dia do Piauí",                                         "estadual", "PI", null, true },
                    { new DateOnly(2025, 12, 19),"Emancipação Política do Paraná",                       "estadual", "PR", null, true },
                    // ── 2026 ──────────────────────────────────────────────────────
                    { new DateOnly(2026, 1, 4),  "Criação do Estado de Rondônia",                        "estadual", "RO", null, true },
                    { new DateOnly(2026, 3, 6),  "Revolução Pernambucana",                               "estadual", "PE", null, true },
                    { new DateOnly(2026, 3, 25), "Data Magna do Ceará",                                  "estadual", "CE", null, true },
                    { new DateOnly(2026, 6, 15), "Aniversário do Estado do Acre",                        "estadual", "AC", null, true },
                    { new DateOnly(2026, 7, 2),  "Independência da Bahia",                               "estadual", "BA", null, true },
                    { new DateOnly(2026, 7, 9),  "Revolução Constitucionalista de 1932",                 "estadual", "SP", null, true },
                    { new DateOnly(2026, 7, 26), "Fundação da Cidade de Goiás",                          "estadual", "GO", null, true },
                    { new DateOnly(2026, 7, 28), "Adesão do Maranhão à Independência do Brasil",         "estadual", "MA", null, true },
                    { new DateOnly(2026, 8, 15), "Adesão do Grão-Pará à Independência do Brasil",        "estadual", "PA", null, true },
                    { new DateOnly(2026, 9, 5),  "Elevação à Categoria de Estado do Amazonas",           "estadual", "AM", null, true },
                    { new DateOnly(2026, 9, 16), "Emancipação Política de Alagoas",                      "estadual", "AL", null, true },
                    { new DateOnly(2026, 9, 20), "Proclamação da República Rio-Grandense",               "estadual", "RS", null, true },
                    { new DateOnly(2026, 10, 3), "Mártires de Cunhaú e Uruaçu",                         "estadual", "RN", null, true },
                    { new DateOnly(2026, 10, 5), "Criação do Estado do Amapá",                           "estadual", "AP", null, true },
                    { new DateOnly(2026, 10, 5), "Criação do Estado de Roraima",                         "estadual", "RR", null, true },
                    { new DateOnly(2026, 10, 5), "Criação do Estado do Tocantins",                       "estadual", "TO", null, true },
                    { new DateOnly(2026, 10, 11),"Criação do Estado de Mato Grosso do Sul",              "estadual", "MS", null, true },
                    { new DateOnly(2026, 10, 19),"Dia do Piauí",                                         "estadual", "PI", null, true },
                    { new DateOnly(2026, 12, 19),"Emancipação Política do Paraná",                       "estadual", "PR", null, true },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM feriados WHERE \"Tipo\" = 'estadual'");
        }
    }
}
