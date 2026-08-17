using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class NabywcaPrzedKorekta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NabywcaPrzedAdresLinia1",
                table: "faktury_sprzedazy",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NabywcaPrzedAdresLinia2",
                table: "faktury_sprzedazy",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NabywcaPrzedKodKraju",
                table: "faktury_sprzedazy",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NabywcaPrzedNazwa",
                table: "faktury_sprzedazy",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NabywcaPrzedNip",
                table: "faktury_sprzedazy",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NabywcaPrzedAdresLinia1",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "NabywcaPrzedAdresLinia2",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "NabywcaPrzedKodKraju",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "NabywcaPrzedNazwa",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "NabywcaPrzedNip",
                table: "faktury_sprzedazy");
        }
    }
}
