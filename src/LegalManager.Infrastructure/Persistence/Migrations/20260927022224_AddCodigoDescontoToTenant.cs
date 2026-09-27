using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodigoDescontoToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoDesconto",
                table: "Tenants",
                type: "text",
                nullable: true);

            // Quem se cadastrou com um código de desconto passa a tê-lo em CodigoDesconto.
            migrationBuilder.Sql("""
                UPDATE "Tenants" t
                SET "CodigoDesconto" = t."VoucherUtilizado"
                FROM "CodigosPromocionais" c
                WHERE c."Codigo" = t."VoucherUtilizado" AND c."DescontoPercentual" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodigoDesconto",
                table: "Tenants");
        }
    }
}
