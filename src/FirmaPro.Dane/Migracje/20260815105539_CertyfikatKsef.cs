using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class CertyfikatKsef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "CertyfikatKsefZaszyfrowany",
                table: "firmy",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertyfikatOdcisk",
                table: "firmy",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertyfikatPodmiot",
                table: "firmy",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CertyfikatWaznyDo",
                table: "firmy",
                type: "timestamp with time zone",
                nullable: true);

            // Wartość domyślna to "Token", a nie pusty ciąg wygenerowany przez
            // narzędzie: firmy założone wcześniej pracują tokenem i pusta
            // nazwa nie odwzorowałaby się z powrotem na żadną metodę.
            migrationBuilder.AddColumn<string>(
                name: "MetodaUwierzytelnienia",
                table: "firmy",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Token");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertyfikatKsefZaszyfrowany",
                table: "firmy");

            migrationBuilder.DropColumn(
                name: "CertyfikatOdcisk",
                table: "firmy");

            migrationBuilder.DropColumn(
                name: "CertyfikatPodmiot",
                table: "firmy");

            migrationBuilder.DropColumn(
                name: "CertyfikatWaznyDo",
                table: "firmy");

            migrationBuilder.DropColumn(
                name: "MetodaUwierzytelnienia",
                table: "firmy");
        }
    }
}
