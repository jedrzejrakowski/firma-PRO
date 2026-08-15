using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class Upo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DataUpoUtc",
                table: "faktury_sprzedazy",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumerSesjiKsef",
                table: "faktury_sprzedazy",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpoXml",
                table: "faktury_sprzedazy",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataUpoUtc",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "NumerSesjiKsef",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "UpoXml",
                table: "faktury_sprzedazy");
        }
    }
}
