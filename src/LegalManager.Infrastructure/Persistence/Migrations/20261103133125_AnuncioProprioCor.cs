using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnuncioProprioCor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cor",
                table: "AnunciosProprios",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AnunciosProprios",
                keyColumn: "Id",
                keyValue: new Guid("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b01"),
                column: "Cor",
                value: "#1e3a8a");

            migrationBuilder.UpdateData(
                table: "AnunciosProprios",
                keyColumn: "Id",
                keyValue: new Guid("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b02"),
                column: "Cor",
                value: "#6d28d9");

            migrationBuilder.UpdateData(
                table: "AnunciosProprios",
                keyColumn: "Id",
                keyValue: new Guid("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b03"),
                column: "Cor",
                value: "#0f766e");

            migrationBuilder.UpdateData(
                table: "AnunciosProprios",
                keyColumn: "Id",
                keyValue: new Guid("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b04"),
                column: "Cor",
                value: "#fbbf24");

            migrationBuilder.UpdateData(
                table: "AnunciosProprios",
                keyColumn: "Id",
                keyValue: new Guid("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b05"),
                column: "Cor",
                value: "#be185d");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cor",
                table: "AnunciosProprios");
        }
    }
}
