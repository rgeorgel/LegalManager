using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificacaoChaveDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChaveDedup",
                table: "Notificacoes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notificacoes_ChaveDedup",
                table: "Notificacoes",
                column: "ChaveDedup",
                unique: true,
                filter: "\"ChaveDedup\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notificacoes_ChaveDedup",
                table: "Notificacoes");

            migrationBuilder.DropColumn(
                name: "ChaveDedup",
                table: "Notificacoes");
        }
    }
}
