using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContatosMelhorias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContatoFiltrosSalvos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Busca = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TipoContato = table.Column<int>(type: "integer", nullable: true),
                    Tipo = table.Column<int>(type: "integer", nullable: true),
                    Tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContatoFiltrosSalvos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContatoFiltrosSalvos_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContatoFiltrosSalvos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContatoVinculos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContatoRelacionadoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContatoVinculos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContatoVinculos_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContatoVinculos_Contatos_ContatoRelacionadoId",
                        column: x => x.ContatoRelacionadoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContatoVinculos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContatoFiltrosSalvos_TenantId_UsuarioId",
                table: "ContatoFiltrosSalvos",
                columns: new[] { "TenantId", "UsuarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContatoFiltrosSalvos_UsuarioId",
                table: "ContatoFiltrosSalvos",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ContatoVinculos_ContatoId",
                table: "ContatoVinculos",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContatoVinculos_ContatoRelacionadoId",
                table: "ContatoVinculos",
                column: "ContatoRelacionadoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContatoVinculos_TenantId_ContatoId",
                table: "ContatoVinculos",
                columns: new[] { "TenantId", "ContatoId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContatoVinculos_TenantId_ContatoRelacionadoId",
                table: "ContatoVinculos",
                columns: new[] { "TenantId", "ContatoRelacionadoId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContatoFiltrosSalvos");

            migrationBuilder.DropTable(
                name: "ContatoVinculos");
        }
    }
}
