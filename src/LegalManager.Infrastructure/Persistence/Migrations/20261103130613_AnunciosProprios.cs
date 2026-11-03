using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnunciosProprios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnunciosProprios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Icone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Titulo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Texto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TextoBotao = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LinkUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnunciosProprios", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AnunciosProprios",
                columns: new[] { "Id", "Ativo", "AtualizadoEm", "CriadoEm", "CriadoPorId", "Icone", "LinkUrl", "Texto", "TextoBotao", "Titulo" },
                values: new object[,]
                {
                    { new Guid("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b01"), true, null, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null, "🚫", "/pages/assinatura.html", "Nos planos pagos do Causify não há publicidade: só você e os seus processos.", "Ver planos", "Trabalhe sem anúncios" },
                    { new Guid("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b02"), true, null, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null, "📰", "/pages/assinatura.html", "No plano Pro, o Causify captura as publicações da sua OAB e você não perde nenhum prazo.", "Conhecer o Pro", "Publicações sem abrir o Diário" },
                    { new Guid("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b03"), true, null, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null, "🗂️", "/pages/assinatura.html", "Organize as tarefas do escritório em colunas e arraste os cartões conforme o trabalho avança. A partir do Plus.", "Conhecer o Plus", "Tarefas em Kanban" },
                    { new Guid("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b04"), true, null, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null, "💰", "/pages/assinatura.html", "Lance honorários, custas e recebimentos em cada processo e saiba quanto o escritório tem a receber. A partir do Plus.", "Conhecer o Plus", "Financeiro por processo" },
                    { new Guid("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b05"), true, null, new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Utc), null, "🤝", "/pages/assinatura.html", "Dê ao cliente um acesso para acompanhar os processos dele, sem ligações perguntando o andamento. A partir do Plus.", "Conhecer o Plus", "Portal do cliente" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnunciosProprios_Ativo",
                table: "AnunciosProprios",
                column: "Ativo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnunciosProprios");
        }
    }
}
