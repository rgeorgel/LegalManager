using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrialAlertaLinkAssinatura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Alerta "Trial expira em N dia(s)" (Tipo 2 = TrialExpirando) apontava para Configurações.
            migrationBuilder.Sql("""
                UPDATE "Notificacoes" SET "Url" = '/pages/assinatura.html'
                WHERE "Tipo" = 2 AND "Url" = '/pages/configuracoes.html';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
