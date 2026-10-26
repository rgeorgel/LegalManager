using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConverterPrazosAgendaEmTarefas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Prazos passam a ser só Tarefas (Tipo=Prazo): eventos de agenda não têm status e não
            // entravam no Kanban. A tarefa herda o Id do evento (referências de auditoria seguem
            // válidas). Prazos anteriores a hoje (Brasília) entram como concluídos — o dashboard
            // já não os contava como pendentes; de hoje em diante, pendentes.
            // Enums: TipoEvento.Prazo=3, TipoTarefa.Prazo=1, PrioridadeTarefa.Media=1,
            // StatusTarefa.Pendente=0 / Concluida=2. DataHora/Prazo estão na hora "de parede".
            migrationBuilder.Sql(@"
                WITH hoje AS (
                    SELECT date_trunc('day', now() AT TIME ZONE 'America/Sao_Paulo') AT TIME ZONE 'UTC' AS inicio
                ),
                prazos AS (
                    SELECT e.*,
                           COALESCE(e.""ResponsavelId"",
                                    (SELECT u.""Id"" FROM ""AspNetUsers"" u
                                     WHERE u.""TenantId"" = e.""TenantId""
                                     ORDER BY u.""CriadoEm"" LIMIT 1)) AS ""Dono""
                    FROM ""Eventos"" e
                    WHERE e.""Tipo"" = 3
                )
                INSERT INTO ""Tarefas"" (""Id"", ""TenantId"", ""ProcessoId"", ""Titulo"", ""Descricao"",
                    ""ResponsavelId"", ""CriadoPorId"", ""Prazo"", ""Prioridade"", ""Status"", ""Tipo"",
                    ""CriadoEm"", ""AtualizadoEm"", ""ConcluidaEm"")
                SELECT p.""Id"", p.""TenantId"", p.""ProcessoId"", p.""Titulo"", p.""Observacoes"",
                    p.""Dono"", p.""Dono"", p.""DataHora"", 1,
                    CASE WHEN p.""DataHora"" < hoje.inicio THEN 2 ELSE 0 END, 1,
                    p.""CriadoEm"", p.""AtualizadoEm"",
                    CASE WHEN p.""DataHora"" < hoje.inicio THEN p.""DataHora"" END
                FROM prazos p CROSS JOIN hoje
                WHERE p.""Dono"" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM ""Tarefas"" t WHERE t.""Id"" = p.""Id"");

                DELETE FROM ""Eventos"" e
                WHERE e.""Tipo"" = 3
                  AND EXISTS (SELECT 1 FROM ""Tarefas"" t WHERE t.""Id"" = e.""Id"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem volta: as tarefas convertidas podem ter sido editadas/concluídas desde então.
        }
    }
}
