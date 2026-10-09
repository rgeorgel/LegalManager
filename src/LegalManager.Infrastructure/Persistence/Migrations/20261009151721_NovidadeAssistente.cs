using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NovidadeAssistente : Migration
    {
        private static readonly Guid Id = new("0b7f3c1e-2a4d-4e5f-9a10-000000000008");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Novidade do assistente de IA. Como as da migration Novidades: inserida só aqui (não via
            // HasData), para o super admin poder editá-la ou excluí-la. Em destaque: abre o modal no
            // dashboard para quem tem plano Plus ou superior; os demais a veem no painel ✨ com o selo
            // "Plus". O link abre o chat (assistente.js trata ?assistente=1).
            var publicadaEm = new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "Novidades",
                columns: new[] { "Id", "Titulo", "Descricao", "LinkUrl", "LinkTexto", "PlanoMinimo", "Destaque", "TourId", "Publicada", "PublicadaEm", "CriadoEm" },
                values: new object[]
                {
                    Id,
                    "Conheça o Assistente de IA ✨",
                    "Pergunte em linguagem natural e o assistente consulta os dados do seu escritório para responder:\n\n" +
                    "- **Prazos e tarefas**: o que vence esta semana, o que está atrasado, o que cada pessoa tem para fazer\n" +
                    "- **Agenda**: audiências, reuniões e perícias dos próximos dias\n" +
                    "- **Processos e contatos**: resumo de um processo, partes, últimos andamentos, clientes por tag\n" +
                    "- **Financeiro e honorários**: resumo do mês, clientes em atraso, parcelas de um contrato\n\n" +
                    "As respostas trazem links para abrir o registro no sistema. Use o botão **✨ Assistente** no canto " +
                    "inferior direito; dá para maximizar a janela para ver tabelas maiores e retomar conversas anteriores em 🕘.\n\n" +
                    "_O assistente só consulta dados — não cria nem altera nada. Confira as informações importantes nos registros._",
                    "/pages/dashboard.html?assistente=1",
                    "Abrir o assistente",
                    3, // PlanoTipo.Plus
                    true,
                    "novidade-assistente",
                    true,
                    publicadaEm,
                    publicadaEm
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "Novidades", keyColumn: "Id", keyValue: Id);
        }
    }
}
