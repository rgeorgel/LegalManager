using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModuloHonorariosContratos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContratoHonorarioId",
                table: "LancamentosFinanceiros",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParcelaHonorarioId",
                table: "LancamentosFinanceiros",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParcelaHonorarioId1",
                table: "LancamentosFinanceiros",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConfiguracoesHonorarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NomeEscritorio = table.Column<string>(type: "text", nullable: true),
                    AdvogadoResponsavel = table.Column<string>(type: "text", nullable: true),
                    OAB = table.Column<string>(type: "text", nullable: true),
                    Endereco = table.Column<string>(type: "text", nullable: true),
                    Telefone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    MetaMensalPadrao = table.Column<decimal>(type: "numeric", nullable: false),
                    PercentualMultaDefault = table.Column<decimal>(type: "numeric", nullable: false),
                    PercentualJurosMensalDefault = table.Column<decimal>(type: "numeric", nullable: false),
                    DiasAvisoVencimento = table.Column<int>(type: "integer", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesHonorarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfiguracoesHonorarios_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContratosHonorarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    NumeroContrato = table.Column<string>(type: "text", nullable: false),
                    Objeto = table.Column<string>(type: "text", nullable: true),
                    ValorTotal = table.Column<decimal>(type: "numeric", nullable: false),
                    FormaPagamento = table.Column<int>(type: "integer", nullable: false),
                    Periodicidade = table.Column<int>(type: "integer", nullable: true),
                    NumeroParcelas = table.Column<int>(type: "integer", nullable: true),
                    DataPrimeiraParcela = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValorEntrada = table.Column<decimal>(type: "numeric", nullable: true),
                    VencimentoEntrada = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PercentualMulta = table.Column<decimal>(type: "numeric", nullable: false),
                    PercentualJurosMensal = table.Column<decimal>(type: "numeric", nullable: false),
                    TipoCobranca = table.Column<string>(type: "text", nullable: false),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DistratoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DistratoMotivo = table.Column<string>(type: "text", nullable: true),
                    DataInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataFim = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContratosHonorarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContratosHonorarios_AspNetUsers_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContratosHonorarios_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContratosHonorarios_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContratosHonorarios_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HistoricosContratoHonorario",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoEvento = table.Column<int>(type: "integer", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DadosAnterioresJson = table.Column<string>(type: "text", nullable: true),
                    DadosNovosJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricosContratoHonorario", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricosContratoHonorario_ContratosHonorarios_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "ContratosHonorarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParcelasHonorarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    IsEntrada = table.Column<bool>(type: "boolean", nullable: false),
                    Vencimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValorOriginal = table.Column<decimal>(type: "numeric", nullable: false),
                    DataPagamento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValorPago = table.Column<decimal>(type: "numeric", nullable: true),
                    Observacao = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LancamentoFinanceiroId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParcelasHonorarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParcelasHonorarios_ContratosHonorarios_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "ContratosHonorarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_ContratoHonorarioId",
                table: "LancamentosFinanceiros",
                column: "ContratoHonorarioId");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_ParcelaHonorarioId1",
                table: "LancamentosFinanceiros",
                column: "ParcelaHonorarioId1");

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracoesHonorarios_TenantId",
                table: "ConfiguracoesHonorarios",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosHonorarios_ContatoId",
                table: "ContratosHonorarios",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosHonorarios_CriadoPorId",
                table: "ContratosHonorarios",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosHonorarios_ProcessoId",
                table: "ContratosHonorarios",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosHonorarios_TenantId",
                table: "ContratosHonorarios",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricosContratoHonorario_ContratoId",
                table: "HistoricosContratoHonorario",
                column: "ContratoId");

            migrationBuilder.CreateIndex(
                name: "IX_ParcelasHonorarios_ContratoId",
                table: "ParcelasHonorarios",
                column: "ContratoId");

            migrationBuilder.AddForeignKey(
                name: "FK_LancamentosFinanceiros_ContratosHonorarios_ContratoHonorari~",
                table: "LancamentosFinanceiros",
                column: "ContratoHonorarioId",
                principalTable: "ContratosHonorarios",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LancamentosFinanceiros_ParcelasHonorarios_ParcelaHonorarioI~",
                table: "LancamentosFinanceiros",
                column: "ParcelaHonorarioId1",
                principalTable: "ParcelasHonorarios",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LancamentosFinanceiros_ContratosHonorarios_ContratoHonorari~",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropForeignKey(
                name: "FK_LancamentosFinanceiros_ParcelasHonorarios_ParcelaHonorarioI~",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropTable(
                name: "ConfiguracoesHonorarios");

            migrationBuilder.DropTable(
                name: "HistoricosContratoHonorario");

            migrationBuilder.DropTable(
                name: "ParcelasHonorarios");

            migrationBuilder.DropTable(
                name: "ContratosHonorarios");

            migrationBuilder.DropIndex(
                name: "IX_LancamentosFinanceiros_ContratoHonorarioId",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropIndex(
                name: "IX_LancamentosFinanceiros_ParcelaHonorarioId1",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropColumn(
                name: "ContratoHonorarioId",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropColumn(
                name: "ParcelaHonorarioId",
                table: "LancamentosFinanceiros");

            migrationBuilder.DropColumn(
                name: "ParcelaHonorarioId1",
                table: "LancamentosFinanceiros");
        }
    }
}
