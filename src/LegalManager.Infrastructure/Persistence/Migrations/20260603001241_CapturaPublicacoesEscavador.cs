using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CapturaPublicacoesEscavador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DiarioId",
                table: "Publicacoes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiarioSigla",
                table: "Publicacoes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FonteCaptura",
                table: "Publicacoes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill omitted: legacy rows default to FonteCaptura=DJe (0), which is acceptable.
            // The IdExterno/HashDje backfill in production may be run manually as a one-off
            // UPDATE if a tenant needs to differentiate manual imports from DJe captures.

            migrationBuilder.AddColumn<string>(
                name: "LinkEscavador",
                table: "Publicacoes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkPdf",
                table: "Publicacoes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemEstado",
                table: "Publicacoes",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OrigemId",
                table: "Publicacoes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snippet",
                table: "Publicacoes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tribunal",
                table: "Publicacoes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UuidExterno",
                table: "Publicacoes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenantOabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimaVerificacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantOabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantOabs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TenantOabs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Publicacoes_TenantId_FonteCaptura",
                table: "Publicacoes",
                columns: new[] { "TenantId", "FonteCaptura" });

            migrationBuilder.CreateIndex(
                name: "IX_Publicacoes_TenantId_UuidExterno",
                table: "Publicacoes",
                columns: new[] { "TenantId", "UuidExterno" },
                unique: true,
                filter: "\"UuidExterno\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TenantOabs_TenantId_Ativo",
                table: "TenantOabs",
                columns: new[] { "TenantId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantOabs_TenantId_Uf_Numero",
                table: "TenantOabs",
                columns: new[] { "TenantId", "Uf", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantOabs_UserId",
                table: "TenantOabs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantOabs");

            migrationBuilder.DropIndex(
                name: "IX_Publicacoes_TenantId_FonteCaptura",
                table: "Publicacoes");

            migrationBuilder.DropIndex(
                name: "IX_Publicacoes_TenantId_UuidExterno",
                table: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "DiarioId",
                table: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "DiarioSigla",
                table: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "FonteCaptura",
                table: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "LinkEscavador",
                table: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "LinkPdf",
                table: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "OrigemEstado",
                table: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "OrigemId",
                table: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "Snippet",
                table: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "Tribunal",
                table: "Publicacoes");

            migrationBuilder.DropColumn(
                name: "UuidExterno",
                table: "Publicacoes");
        }
    }
}
