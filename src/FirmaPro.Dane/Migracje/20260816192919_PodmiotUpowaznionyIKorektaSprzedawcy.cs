using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class PodmiotUpowaznionyIKorektaSprzedawcy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SprzedawcaPrzedAdresLinia1",
                table: "faktury_sprzedazy",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SprzedawcaPrzedAdresLinia2",
                table: "faktury_sprzedazy",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SprzedawcaPrzedKodKraju",
                table: "faktury_sprzedazy",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SprzedawcaPrzedNazwa",
                table: "faktury_sprzedazy",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SprzedawcaPrzedNip",
                table: "faktury_sprzedazy",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpowaznionyAdresLinia1",
                table: "faktury_sprzedazy",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpowaznionyAdresLinia2",
                table: "faktury_sprzedazy",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpowaznionyEmail",
                table: "faktury_sprzedazy",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpowaznionyKodKraju",
                table: "faktury_sprzedazy",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpowaznionyNazwa",
                table: "faktury_sprzedazy",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpowaznionyNip",
                table: "faktury_sprzedazy",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpowaznionyRola",
                table: "faktury_sprzedazy",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpowaznionyTelefon",
                table: "faktury_sprzedazy",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SprzedawcaPrzedAdresLinia1",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "SprzedawcaPrzedAdresLinia2",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "SprzedawcaPrzedKodKraju",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "SprzedawcaPrzedNazwa",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "SprzedawcaPrzedNip",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "UpowaznionyAdresLinia1",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "UpowaznionyAdresLinia2",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "UpowaznionyEmail",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "UpowaznionyKodKraju",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "UpowaznionyNazwa",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "UpowaznionyNip",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "UpowaznionyRola",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "UpowaznionyTelefon",
                table: "faktury_sprzedazy");
        }
    }
}
