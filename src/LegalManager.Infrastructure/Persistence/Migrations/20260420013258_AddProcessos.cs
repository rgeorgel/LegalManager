using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Processos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroCNJ = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Tribunal = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Vara = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Comarca = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    AreaDireito = table.Column<int>(type: "integer", nullable: false),
                    TipoAcao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Fase = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ValorCausa = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    AdvogadoResponsavelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Monitorado = table.Column<bool>(type: "boolean", nullable: false),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    Decisao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Resultado = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EncerradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Processos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Processos_AspNetUsers_AdvogadoResponsavelId",
                        column: x => x.AdvogadoResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Processos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Andamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    Fonte = table.Column<int>(type: "integer", nullable: false),
                    DescricaoTraduzidaIA = table.Column<string>(type: "text", nullable: true),
                    RegistradoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Andamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Andamentos_AspNetUsers_RegistradoPorId",
                        column: x => x.RegistradoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Andamentos_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessoPartes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoParte = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessoPartes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessoPartes_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessoPartes_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Andamentos_ProcessoId_Data",
                table: "Andamentos",
                columns: new[] { "ProcessoId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_Andamentos_RegistradoPorId",
                table: "Andamentos",
                column: "RegistradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Andamentos_TenantId",
                table: "Andamentos",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessoPartes_ContatoId",
                table: "ProcessoPartes",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessoPartes_ProcessoId_ContatoId_TipoParte",
                table: "ProcessoPartes",
                columns: new[] { "ProcessoId", "ContatoId", "TipoParte" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Processos_AdvogadoResponsavelId",
                table: "Processos",
                column: "AdvogadoResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_Processos_TenantId_NumeroCNJ",
                table: "Processos",
                columns: new[] { "TenantId", "NumeroCNJ" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Processos_TenantId_Status",
                table: "Processos",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Andamentos");

            migrationBuilder.DropTable(
                name: "ProcessoPartes");

            migrationBuilder.DropTable(
                name: "Processos");
        }
    }
}
