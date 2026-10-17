using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddValorMensalAssinatura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ValorMensalAssinatura",
                table: "Tenants",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            // Até aqui nenhum preço tinha mudado: toda assinatura Stripe cobra a tabela original
            // (Pro = 1 → 50, Plus = 3 → 20, Max = 4 → 125).
            migrationBuilder.Sql("""
                UPDATE "Tenants"
                SET "ValorMensalAssinatura" = CASE "Plano" WHEN 1 THEN 50 WHEN 3 THEN 20 WHEN 4 THEN 125 END
                WHERE "StripeSubscriptionId" IS NOT NULL AND "Plano" IN (1, 3, 4);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValorMensalAssinatura",
                table: "Tenants");
        }
    }
}
