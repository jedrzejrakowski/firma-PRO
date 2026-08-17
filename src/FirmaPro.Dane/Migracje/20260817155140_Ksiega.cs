using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class Ksiega : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FormaOpodatkowania",
                table: "firmy",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "StawkaRyczaltu",
                table: "firmy",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "KolumnaKpir",
                table: "faktury_zakupu",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "KosztPodatkowy",
                table: "faktury_zakupu",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "StawkaRyczaltu",
                table: "faktury_sprzedazy",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "zapisy_ksiegi",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    NumerDowodu = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Kontrahent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Adres = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Opis = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Kolumna = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kwota = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StawkaRyczaltu = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Uwagi = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zapisy_ksiegi", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_zapisy_ksiegi_FirmaId_Data",
                table: "zapisy_ksiegi",
                columns: new[] { "FirmaId", "Data" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "zapisy_ksiegi");

            migrationBuilder.DropColumn(
                name: "FormaOpodatkowania",
                table: "firmy");

            migrationBuilder.DropColumn(
                name: "StawkaRyczaltu",
                table: "firmy");

            migrationBuilder.DropColumn(
                name: "KolumnaKpir",
                table: "faktury_zakupu");

            migrationBuilder.DropColumn(
                name: "KosztPodatkowy",
                table: "faktury_zakupu");

            migrationBuilder.DropColumn(
                name: "StawkaRyczaltu",
                table: "faktury_sprzedazy");
        }
    }
}
