using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecorrenciaTarefas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecorrenciaId",
                table: "Tarefas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecorrenciasTarefa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Frequencia = table.Column<int>(type: "integer", nullable: false),
                    Intervalo = table.Column<int>(type: "integer", nullable: false),
                    DiasSemana = table.Column<int>(type: "integer", nullable: false),
                    DiaDoMes = table.Column<int>(type: "integer", nullable: true),
                    Ancora = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TerminaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimaGerada = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Prioridade = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    ResponsavelId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecorrenciasTarefa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecorrenciasTarefa_AspNetUsers_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecorrenciasTarefa_AspNetUsers_ResponsavelId",
                        column: x => x.ResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecorrenciasTarefa_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecorrenciasTarefa_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecorrenciasTarefa_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_RecorrenciaId",
                table: "Tarefas",
                column: "RecorrenciaId");

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_Ativa_UltimaGerada",
                table: "RecorrenciasTarefa",
                columns: new[] { "Ativa", "UltimaGerada" });

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_ContatoId",
                table: "RecorrenciasTarefa",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_CriadoPorId",
                table: "RecorrenciasTarefa",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_ProcessoId",
                table: "RecorrenciasTarefa",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_ResponsavelId",
                table: "RecorrenciasTarefa",
                column: "ResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_TenantId",
                table: "RecorrenciasTarefa",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tarefas_RecorrenciasTarefa_RecorrenciaId",
                table: "Tarefas",
                column: "RecorrenciaId",
                principalTable: "RecorrenciasTarefa",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tarefas_RecorrenciasTarefa_RecorrenciaId",
                table: "Tarefas");

            migrationBuilder.DropTable(
                name: "RecorrenciasTarefa");

            migrationBuilder.DropIndex(
                name: "IX_Tarefas_RecorrenciaId",
                table: "Tarefas");

            migrationBuilder.DropColumn(
                name: "RecorrenciaId",
                table: "Tarefas");
        }
    }
}
