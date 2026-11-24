using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrecedentesTextoBusca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Precedentes_Texto",
                table: "Precedentes");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "TextoBusca",
                table: "Precedentes",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('portuguese', \"Texto\")",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Precedentes_TextoBusca",
                table: "Precedentes",
                column: "TextoBusca")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Precedentes_TextoBusca",
                table: "Precedentes");

            migrationBuilder.DropColumn(
                name: "TextoBusca",
                table: "Precedentes");

            migrationBuilder.CreateIndex(
                name: "IX_Precedentes_Texto",
                table: "Precedentes",
                column: "Texto")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:TsVectorConfig", "portuguese");
        }
    }
}
