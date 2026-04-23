using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Modulo10_IA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IAHabilitada",
                table: "Contatos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CreditosAI",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    QuantidadeTotal = table.Column<int>(type: "integer", nullable: false),
                    QuantidadeUsada = table.Column<int>(type: "integer", nullable: false),
                    Origem = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditosAI", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditosAI_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PecasGeradas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    GeradoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DescricaoSolicitacao = table.Column<string>(type: "text", nullable: false),
                    ConteudoGerado = table.Column<string>(type: "text", nullable: false),
                    JurisprudenciaCitada = table.Column<string>(type: "text", nullable: true),
                    TesesSugeridas = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PecasGeradas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PecasGeradas_AspNetUsers_GeradoPorId",
                        column: x => x.GeradoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PecasGeradas_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TraducoesAndamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AndamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SolicitadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    TextoOriginal = table.Column<string>(type: "text", nullable: false),
                    TextoTraduzido = table.Column<string>(type: "text", nullable: false),
                    EnviadoAoCliente = table.Column<bool>(type: "boolean", nullable: false),
                    RevisadoPreviamente = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TraducoesAndamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TraducoesAndamentos_Andamentos_AndamentoId",
                        column: x => x.AndamentoId,
                        principalTable: "Andamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TraducoesAndamentos_AspNetUsers_SolicitadoPorId",
                        column: x => x.SolicitadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TraducoesAndamentos_Contatos_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Contatos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditosAI_TenantId_Tipo",
                table: "CreditosAI",
                columns: new[] { "TenantId", "Tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PecasGeradas_GeradoPorId",
                table: "PecasGeradas",
                column: "GeradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_PecasGeradas_ProcessoId",
                table: "PecasGeradas",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_PecasGeradas_TenantId",
                table: "PecasGeradas",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PecasGeradas_TenantId_ProcessoId",
                table: "PecasGeradas",
                columns: new[] { "TenantId", "ProcessoId" });

            migrationBuilder.CreateIndex(
                name: "IX_PecasGeradas_TenantId_Tipo",
                table: "PecasGeradas",
                columns: new[] { "TenantId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_TraducoesAndamentos_AndamentoId",
                table: "TraducoesAndamentos",
                column: "AndamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_TraducoesAndamentos_ClienteId",
                table: "TraducoesAndamentos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_TraducoesAndamentos_SolicitadoPorId",
                table: "TraducoesAndamentos",
                column: "SolicitadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_TraducoesAndamentos_TenantId_AndamentoId",
                table: "TraducoesAndamentos",
                columns: new[] { "TenantId", "AndamentoId" });

            migrationBuilder.CreateIndex(
                name: "IX_TraducoesAndamentos_TenantId_ClienteId",
                table: "TraducoesAndamentos",
                columns: new[] { "TenantId", "ClienteId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditosAI");

            migrationBuilder.DropTable(
                name: "PecasGeradas");

            migrationBuilder.DropTable(
                name: "TraducoesAndamentos");

            migrationBuilder.DropColumn(
                name: "IAHabilitada",
                table: "Contatos");
        }
    }
}
