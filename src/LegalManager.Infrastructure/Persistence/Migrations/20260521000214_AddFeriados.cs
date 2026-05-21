using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeriados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feriados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Municipio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feriados", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_feriados_ativo",
                table: "feriados",
                column: "Ativo");

            migrationBuilder.CreateIndex(
                name: "idx_feriados_data",
                table: "feriados",
                column: "Data");

            migrationBuilder.CreateIndex(
                name: "idx_feriados_tipo",
                table: "feriados",
                column: "Tipo");

            migrationBuilder.CreateIndex(
                name: "IX_feriados_Data_Tipo_Uf_Municipio",
                table: "feriados",
                columns: new[] { "Data", "Tipo", "Uf", "Municipio" },
                unique: true);

            // Seed: feriados nacionais 2025 e 2026
            migrationBuilder.InsertData(
                table: "feriados",
                columns: ["Data", "Nome", "Tipo", "Uf", "Municipio", "Ativo"],
                values: new object[,]
                {
                    // 2025
                    { new DateOnly(2025, 1, 1),  "Confraternização Universal",  "nacional", null, null, true },
                    { new DateOnly(2025, 4, 18), "Sexta-feira Santa",           "nacional", null, null, true },
                    { new DateOnly(2025, 4, 21), "Tiradentes",                  "nacional", null, null, true },
                    { new DateOnly(2025, 5, 1),  "Dia do Trabalho",             "nacional", null, null, true },
                    { new DateOnly(2025, 6, 19), "Corpus Christi",              "nacional", null, null, true },
                    { new DateOnly(2025, 9, 7),  "Independência do Brasil",     "nacional", null, null, true },
                    { new DateOnly(2025, 10, 12),"Nossa Senhora Aparecida",     "nacional", null, null, true },
                    { new DateOnly(2025, 11, 2), "Finados",                     "nacional", null, null, true },
                    { new DateOnly(2025, 11, 15),"Proclamação da República",    "nacional", null, null, true },
                    { new DateOnly(2025, 11, 20),"Consciência Negra",           "nacional", null, null, true },
                    { new DateOnly(2025, 12, 25),"Natal",                       "nacional", null, null, true },
                    // 2026
                    { new DateOnly(2026, 1, 1),  "Confraternização Universal",  "nacional", null, null, true },
                    { new DateOnly(2026, 4, 3),  "Sexta-feira Santa",           "nacional", null, null, true },
                    { new DateOnly(2026, 4, 21), "Tiradentes",                  "nacional", null, null, true },
                    { new DateOnly(2026, 5, 1),  "Dia do Trabalho",             "nacional", null, null, true },
                    { new DateOnly(2026, 6, 4),  "Corpus Christi",              "nacional", null, null, true },
                    { new DateOnly(2026, 9, 7),  "Independência do Brasil",     "nacional", null, null, true },
                    { new DateOnly(2026, 10, 12),"Nossa Senhora Aparecida",     "nacional", null, null, true },
                    { new DateOnly(2026, 11, 2), "Finados",                     "nacional", null, null, true },
                    { new DateOnly(2026, 11, 15),"Proclamação da República",    "nacional", null, null, true },
                    { new DateOnly(2026, 11, 20),"Consciência Negra",           "nacional", null, null, true },
                    { new DateOnly(2026, 12, 25),"Natal",                       "nacional", null, null, true },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feriados");
        }
    }
}
