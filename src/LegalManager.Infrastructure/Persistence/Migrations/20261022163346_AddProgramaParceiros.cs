using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgramaParceiros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParceiroId",
                table: "Tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ParceiroVinculadoEm",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Parceiros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Telefone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Documento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PercentualComissao = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    MesesComissao = table.Column<int>(type: "integer", nullable: true),
                    ChavePix = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Observacoes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    SenhaHash = table.Column<string>(type: "text", nullable: true),
                    TokenSenha = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TokenSenhaExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoAcessoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parceiros", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SolicitacoesPagamentoParceiros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParceiroId = table.Column<Guid>(type: "uuid", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChavePix = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ObservacaoParceiro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SolicitadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvidaPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Resposta = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitacoesPagamentoParceiros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SolicitacoesPagamentoParceiros_Parceiros_ParceiroId",
                        column: x => x.ParceiroId,
                        principalTable: "Parceiros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ComissoesParceiros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParceiroId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NomeEscritorio = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FaturamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValorPagamento = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Percentual = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    PagoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SolicitacaoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComissoesParceiros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComissoesParceiros_Parceiros_ParceiroId",
                        column: x => x.ParceiroId,
                        principalTable: "Parceiros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComissoesParceiros_SolicitacoesPagamentoParceiros_Solicitac~",
                        column: x => x.SolicitacaoId,
                        principalTable: "SolicitacoesPagamentoParceiros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ParceiroId",
                table: "Tenants",
                column: "ParceiroId");

            migrationBuilder.CreateIndex(
                name: "IX_ComissoesParceiros_FaturamentoId",
                table: "ComissoesParceiros",
                column: "FaturamentoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComissoesParceiros_ParceiroId_TenantId",
                table: "ComissoesParceiros",
                columns: new[] { "ParceiroId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ComissoesParceiros_SolicitacaoId",
                table: "ComissoesParceiros",
                column: "SolicitacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_Parceiros_Codigo",
                table: "Parceiros",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parceiros_Email",
                table: "Parceiros",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parceiros_TokenSenha",
                table: "Parceiros",
                column: "TokenSenha");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesPagamentoParceiros_ParceiroId_Status",
                table: "SolicitacoesPagamentoParceiros",
                columns: new[] { "ParceiroId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_Parceiros_ParceiroId",
                table: "Tenants",
                column: "ParceiroId",
                principalTable: "Parceiros",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_Parceiros_ParceiroId",
                table: "Tenants");

            migrationBuilder.DropTable(
                name: "ComissoesParceiros");

            migrationBuilder.DropTable(
                name: "SolicitacoesPagamentoParceiros");

            migrationBuilder.DropTable(
                name: "Parceiros");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_ParceiroId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ParceiroId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ParceiroVinculadoEm",
                table: "Tenants");
        }
    }
}
