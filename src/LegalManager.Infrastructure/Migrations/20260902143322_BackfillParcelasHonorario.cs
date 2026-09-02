using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Migrations
{
    [ExcludeFromCodeCoverage]
    public partial class BackfillParcelasHonorario : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""ParcelasHonorarios"" p
                SET
                    ""Status"" = 2,
                    ""DataPagamento"" = l.""DataPagamento"",
                    ""ValorPago"" = l.""Valor"",
                    ""LancamentoFinanceiroId"" = l.""Id""
                FROM ""LancamentosFinanceiros"" l
                WHERE l.""ParcelaHonorarioId"" = p.""Id""
                  AND l.""Status"" = 2
                  AND p.""Status"" <> 2
                  AND p.""Status"" <> 4;
            ");

            migrationBuilder.Sql(@"
                UPDATE ""ParcelasHonorarios"" p
                SET
                    ""Status"" = 1,
                    ""DataPagamento"" = NULL,
                    ""ValorPago"" = NULL,
                    ""LancamentoFinanceiroId"" = NULL
                FROM ""LancamentosFinanceiros"" l
                WHERE l.""ParcelaHonorarioId"" = p.""Id""
                  AND l.""Status"" = 3
                  AND p.""LancamentoFinanceiroId"" = l.""Id""
                  AND p.""Status"" = 2;
            ");

            migrationBuilder.Sql(@"
                UPDATE ""ContratosHonorarios"" c
                SET ""Status"" = 3
                WHERE ""Status"" NOT IN (5, 6)
                  AND NOT EXISTS (
                      SELECT 1 FROM ""ParcelasHonorarios"" p
                      WHERE p.""ContratoId"" = c.""Id""
                        AND p.""Status"" NOT IN (2, 4)
                  );
            ");

            migrationBuilder.Sql(@"
                UPDATE ""ContratosHonorarios"" c
                SET ""Status"" = 4
                WHERE ""Status"" NOT IN (3, 5, 6)
                  AND EXISTS (
                      SELECT 1 FROM ""ParcelasHonorarios"" p
                      WHERE p.""ContratoId"" = c.""Id""
                        AND p.""Status"" NOT IN (2, 4)
                        AND p.""Vencimento"" < (NOW() AT TIME ZONE 'UTC')::date
                  );
            ");

            migrationBuilder.Sql(@"
                UPDATE ""ContratosHonorarios"" c
                SET ""Status"" = 1
                WHERE ""Status"" NOT IN (5, 6)
                  AND NOT EXISTS (
                      SELECT 1 FROM ""ParcelasHonorarios"" p
                      WHERE p.""ContratoId"" = c.""Id""
                        AND p.""Status"" NOT IN (2, 4)
                        AND p.""Vencimento"" < (NOW() AT TIME ZONE 'UTC')::date
                  )
                  AND EXISTS (
                      SELECT 1 FROM ""ParcelasHonorarios"" p
                      WHERE p.""ContratoId"" = c.""Id""
                        AND p.""Status"" NOT IN (2, 4)
                  );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
