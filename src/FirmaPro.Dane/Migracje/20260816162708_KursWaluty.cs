using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class KursWaluty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KursTabela",
                table: "faktury_sprzedazy",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KursWaluty",
                table: "faktury_sprzedazy",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "KursZDnia",
                table: "faktury_sprzedazy",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KursTabela",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "KursWaluty",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "KursZDnia",
                table: "faktury_sprzedazy");
        }
    }
}
