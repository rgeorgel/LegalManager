using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferenciasNotificacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PreferenciasNotificacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TarefasInApp = table.Column<bool>(type: "boolean", nullable: false),
                    TarefasEmail = table.Column<bool>(type: "boolean", nullable: false),
                    EventosInApp = table.Column<bool>(type: "boolean", nullable: false),
                    EventosEmail = table.Column<bool>(type: "boolean", nullable: false),
                    PrazosInApp = table.Column<bool>(type: "boolean", nullable: false),
                    PrazosEmail = table.Column<bool>(type: "boolean", nullable: false),
                    PublicacoesInApp = table.Column<bool>(type: "boolean", nullable: false),
                    PublicacoesEmail = table.Column<bool>(type: "boolean", nullable: false),
                    TrialInApp = table.Column<bool>(type: "boolean", nullable: false),
                    GeralInApp = table.Column<bool>(type: "boolean", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreferenciasNotificacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreferenciasNotificacoes_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PreferenciasNotificacoes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PreferenciasNotificacoes_TenantId_UsuarioId",
                table: "PreferenciasNotificacoes",
                columns: new[] { "TenantId", "UsuarioId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PreferenciasNotificacoes_UsuarioId",
                table: "PreferenciasNotificacoes",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PreferenciasNotificacoes");
        }
    }
}
