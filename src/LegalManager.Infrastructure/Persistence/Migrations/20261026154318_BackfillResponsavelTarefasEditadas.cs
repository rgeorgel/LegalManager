using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillResponsavelTarefasEditadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Editar pelo detalhe do processo apagava o responsável (PUT sem ResponsavelId).
            migrationBuilder.Sql(@"
                UPDATE ""Tarefas""
                SET ""ResponsavelId"" = ""CriadoPorId""
                WHERE ""ResponsavelId"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
