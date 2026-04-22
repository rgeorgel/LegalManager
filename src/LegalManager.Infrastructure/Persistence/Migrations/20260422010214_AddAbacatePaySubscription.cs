using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAbacatePaySubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AbacatePayBillingId",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeriodoBilling",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanoExpiraEm",
                table: "Tenants",
                type: "timestamp without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbacatePayBillingId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PeriodoBilling",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PlanoExpiraEm",
                table: "Tenants");
        }
    }
}
