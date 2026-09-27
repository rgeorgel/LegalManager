using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeriados2027 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Correção: Nossa Senhora da Penha (ES) é móvel (Páscoa + 8); em 2026 cai em 13/04,
            // não em 28/04 como semeado em AddFeriadosEstaduaisEsPb.
            migrationBuilder.Sql(
                "UPDATE feriados SET \"Data\" = DATE '2026-04-13', \"AtualizadoEm\" = NOW() " +
                "WHERE \"TenantId\" IS NULL AND \"Tipo\" = 'estadual' AND \"Uf\" = 'ES' " +
                "AND \"Nome\" = 'Nossa Senhora da Penha' AND \"Data\" = DATE '2026-04-28'");

            // Feriados nacionais e estaduais — 2027 (mesmo conjunto semeado para 2025/2026).
            // Móveis a partir da Páscoa em 28/03/2027: Sexta-feira Santa (-2), Corpus Christi (+60),
            // Nossa Senhora da Penha/ES (+8).
            migrationBuilder.InsertData(
                table: "feriados",
                columns: ["Data", "Nome", "Tipo", "Uf", "Municipio", "Ativo"],
                values: new object[,]
                {
                    // ── Nacionais ─────────────────────────────────────────────────
                    { new DateOnly(2027, 1, 1),  "Confraternização Universal",  "nacional", null, null, true },
                    { new DateOnly(2027, 3, 26), "Sexta-feira Santa",           "nacional", null, null, true },
                    { new DateOnly(2027, 4, 21), "Tiradentes",                  "nacional", null, null, true },
                    { new DateOnly(2027, 5, 1),  "Dia do Trabalho",             "nacional", null, null, true },
                    { new DateOnly(2027, 5, 27), "Corpus Christi",              "nacional", null, null, true },
                    { new DateOnly(2027, 9, 7),  "Independência do Brasil",     "nacional", null, null, true },
                    { new DateOnly(2027, 10, 12),"Nossa Senhora Aparecida",     "nacional", null, null, true },
                    { new DateOnly(2027, 11, 2), "Finados",                     "nacional", null, null, true },
                    { new DateOnly(2027, 11, 15),"Proclamação da República",    "nacional", null, null, true },
                    { new DateOnly(2027, 11, 20),"Consciência Negra",           "nacional", null, null, true },
                    { new DateOnly(2027, 12, 25),"Natal",                       "nacional", null, null, true },
                    // ── Estaduais ─────────────────────────────────────────────────
                    { new DateOnly(2027, 1, 4),  "Criação do Estado de Rondônia",                        "estadual", "RO", null, true },
                    { new DateOnly(2027, 1, 20), "São Sebastião",                                        "estadual", "RJ", null, true },
                    { new DateOnly(2027, 3, 6),  "Revolução Pernambucana",                               "estadual", "PE", null, true },
                    { new DateOnly(2027, 3, 25), "Data Magna do Ceará",                                  "estadual", "CE", null, true },
                    { new DateOnly(2027, 4, 5),  "Nossa Senhora da Penha",                               "estadual", "ES", null, true },
                    { new DateOnly(2027, 4, 23), "São Jorge",                                            "estadual", "RJ", null, true },
                    { new DateOnly(2027, 6, 15), "Aniversário do Estado do Acre",                        "estadual", "AC", null, true },
                    { new DateOnly(2027, 7, 2),  "Independência da Bahia",                               "estadual", "BA", null, true },
                    { new DateOnly(2027, 7, 8),  "Emancipação Política de Sergipe",                      "estadual", "SE", null, true },
                    { new DateOnly(2027, 7, 9),  "Revolução Constitucionalista de 1932",                 "estadual", "SP", null, true },
                    { new DateOnly(2027, 7, 26), "Fundação da Cidade de Goiás",                          "estadual", "GO", null, true },
                    { new DateOnly(2027, 7, 26), "João Pessoa",                                          "estadual", "PB", null, true },
                    { new DateOnly(2027, 7, 28), "Adesão do Maranhão à Independência do Brasil",         "estadual", "MA", null, true },
                    { new DateOnly(2027, 8, 5),  "Fundação da Paraíba",                                  "estadual", "PB", null, true },
                    { new DateOnly(2027, 8, 11), "Criação da Província de Santa Catarina",               "estadual", "SC", null, true },
                    { new DateOnly(2027, 8, 15), "Adesão do Grão-Pará à Independência do Brasil",        "estadual", "PA", null, true },
                    { new DateOnly(2027, 8, 24), "Dia da Inconfidência Mineira",                         "estadual", "MG", null, true },
                    { new DateOnly(2027, 9, 5),  "Elevação à Categoria de Estado do Amazonas",           "estadual", "AM", null, true },
                    { new DateOnly(2027, 9, 16), "Emancipação Política de Alagoas",                      "estadual", "AL", null, true },
                    { new DateOnly(2027, 9, 20), "Proclamação da República Rio-Grandense",               "estadual", "RS", null, true },
                    { new DateOnly(2027, 10, 3), "Mártires de Cunhaú e Uruaçu",                         "estadual", "RN", null, true },
                    { new DateOnly(2027, 10, 5), "Criação do Estado do Amapá",                           "estadual", "AP", null, true },
                    { new DateOnly(2027, 10, 5), "Criação do Estado de Roraima",                         "estadual", "RR", null, true },
                    { new DateOnly(2027, 10, 5), "Criação do Estado do Tocantins",                       "estadual", "TO", null, true },
                    { new DateOnly(2027, 10, 11),"Criação do Estado de Mato Grosso do Sul",              "estadual", "MS", null, true },
                    { new DateOnly(2027, 10, 19),"Dia do Piauí",                                         "estadual", "PI", null, true },
                    { new DateOnly(2027, 12, 19),"Emancipação Política do Paraná",                       "estadual", "PR", null, true },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM feriados WHERE \"TenantId\" IS NULL " +
                "AND \"Tipo\" IN ('nacional', 'estadual') " +
                "AND \"Data\" BETWEEN DATE '2027-01-01' AND DATE '2027-12-31'");

            migrationBuilder.Sql(
                "UPDATE feriados SET \"Data\" = DATE '2026-04-28', \"AtualizadoEm\" = NOW() " +
                "WHERE \"TenantId\" IS NULL AND \"Tipo\" = 'estadual' AND \"Uf\" = 'ES' " +
                "AND \"Nome\" = 'Nossa Senhora da Penha' AND \"Data\" = DATE '2026-04-13'");
        }
    }
}
