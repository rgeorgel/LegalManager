using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAtividades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Eventos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DataHora = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataHoraFim = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Local = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ResponsavelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Observacoes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Eventos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Eventos_AspNetUsers_ResponsavelId",
                        column: x => x.ResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Eventos_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Eventos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tarefas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ResponsavelId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prazo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Prioridade = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcluidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tarefas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tarefas_AspNetUsers_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tarefas_AspNetUsers_ResponsavelId",
                        column: x => x.ResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tarefas_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tarefas_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tarefas_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TarefaTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TarefaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TarefaTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TarefaTags_Tarefas_TarefaId",
                        column: x => x.TarefaId,
                        principalTable: "Tarefas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_ProcessoId",
                table: "Eventos",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_ResponsavelId",
                table: "Eventos",
                column: "ResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_TenantId_DataHora",
                table: "Eventos",
                columns: new[] { "TenantId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_TenantId_Tipo",
                table: "Eventos",
                columns: new[] { "TenantId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_ContatoId",
                table: "Tarefas",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_CriadoPorId",
                table: "Tarefas",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_ProcessoId",
                table: "Tarefas",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_ResponsavelId",
                table: "Tarefas",
                column: "ResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_TenantId_Prazo",
                table: "Tarefas",
                columns: new[] { "TenantId", "Prazo" });

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_TenantId_Status",
                table: "Tarefas",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TarefaTags_TarefaId_Tag",
                table: "TarefaTags",
                columns: new[] { "TarefaId", "Tag" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Eventos");

            migrationBuilder.DropTable(
                name: "TarefaTags");

            migrationBuilder.DropTable(
                name: "Tarefas");
        }
    }
}
