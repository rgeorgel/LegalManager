using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssinaturaDigital : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventosAssinaturaRecebidos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    RecebidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosAssinaturaRecebidos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosAssinaturaRecebidos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntegracoesAssinatura",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiKeyCifrada = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ApiKeyFinal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    WebhookSecretCifrado = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OrganizacaoNome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PlanoNome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ConectadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConectadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimoWebhookEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegracoesAssinatura", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegracoesAssinatura_AspNetUsers_ConectadoPorId",
                        column: x => x.ConectadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IntegracoesAssinatura_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MapeamentosModeloAssinatura",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FontesJson = table.Column<string>(type: "text", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapeamentosModeloAssinatura", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MapeamentosModeloAssinatura_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SolicitacoesAssinatura",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoHonorarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContratoExternoId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NumeroExterno = table.Column<int>(type: "integer", nullable: true),
                    TemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TemplateNome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SignatariosJson = table.Column<string>(type: "text", nullable: false),
                    Erro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MotivoRecusa = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DocumentoAssinadoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnviadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcluidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AtualizadoExternoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConferidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitacoesAssinatura", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SolicitacoesAssinatura_AspNetUsers_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SolicitacoesAssinatura_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SolicitacoesAssinatura_ContratosHonorarios_ContratoHonorari~",
                        column: x => x.ContratoHonorarioId,
                        principalTable: "ContratosHonorarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SolicitacoesAssinatura_Documentos_DocumentoAssinadoId",
                        column: x => x.DocumentoAssinadoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SolicitacoesAssinatura_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SolicitacoesAssinatura_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosAssinaturaRecebidos_RecebidoEm",
                table: "EventosAssinaturaRecebidos",
                column: "RecebidoEm");

            migrationBuilder.CreateIndex(
                name: "IX_EventosAssinaturaRecebidos_TenantId",
                table: "EventosAssinaturaRecebidos",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegracoesAssinatura_ConectadoPorId",
                table: "IntegracoesAssinatura",
                column: "ConectadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegracoesAssinatura_TenantId",
                table: "IntegracoesAssinatura",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MapeamentosModeloAssinatura_TenantId_TemplateId",
                table: "MapeamentosModeloAssinatura",
                columns: new[] { "TenantId", "TemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesAssinatura_ContatoId",
                table: "SolicitacoesAssinatura",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesAssinatura_ContratoHonorarioId",
                table: "SolicitacoesAssinatura",
                column: "ContratoHonorarioId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesAssinatura_CriadoPorId",
                table: "SolicitacoesAssinatura",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesAssinatura_DocumentoAssinadoId",
                table: "SolicitacoesAssinatura",
                column: "DocumentoAssinadoId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesAssinatura_ProcessoId",
                table: "SolicitacoesAssinatura",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesAssinatura_Status",
                table: "SolicitacoesAssinatura",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesAssinatura_TenantId_ContratoExternoId",
                table: "SolicitacoesAssinatura",
                columns: new[] { "TenantId", "ContratoExternoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesAssinatura_TenantId_ContratoHonorarioId",
                table: "SolicitacoesAssinatura",
                columns: new[] { "TenantId", "ContratoHonorarioId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosAssinaturaRecebidos");

            migrationBuilder.DropTable(
                name: "IntegracoesAssinatura");

            migrationBuilder.DropTable(
                name: "MapeamentosModeloAssinatura");

            migrationBuilder.DropTable(
                name: "SolicitacoesAssinatura");
        }
    }
}
