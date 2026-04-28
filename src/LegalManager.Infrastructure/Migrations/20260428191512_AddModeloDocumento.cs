using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModeloDocumento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ModeloDocumentoId",
                table: "Documentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ModelosDocumento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    Conteudo = table.Column<string>(type: "text", nullable: false),
                    Variaveis = table.Column<string>(type: "text", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelosDocumento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelosDocumento_AspNetUsers_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ModelosDocumento_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_ModeloDocumentoId",
                table: "Documentos",
                column: "ModeloDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelosDocumento_CriadoPorId",
                table: "ModelosDocumento",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelosDocumento_TenantId",
                table: "ModelosDocumento",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documentos_ModelosDocumento_ModeloDocumentoId",
                table: "Documentos",
                column: "ModeloDocumentoId",
                principalTable: "ModelosDocumento",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documentos_ModelosDocumento_ModeloDocumentoId",
                table: "Documentos");

            migrationBuilder.DropTable(
                name: "ModelosDocumento");

            migrationBuilder.DropIndex(
                name: "IX_Documentos_ModeloDocumentoId",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "ModeloDocumentoId",
                table: "Documentos");
        }
    }
}
