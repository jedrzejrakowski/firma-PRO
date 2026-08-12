using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class ZaliczkiIZamowienia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pozycje_zamowien",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FakturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    NrWiersza = table.Column<int>(type: "integer", nullable: false),
                    Nazwa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Jednostka = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Ilosc = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CenaNetto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    KodStawki = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Gtu = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pozycje_zamowien", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pozycje_zamowien_faktury_sprzedazy_FakturaId",
                        column: x => x.FakturaId,
                        principalTable: "faktury_sprzedazy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rozliczone_zaliczki",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FakturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZaliczkowaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DataWystawienia = table.Column<DateOnly>(type: "date", nullable: false),
                    NumerKsef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Brutto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rozliczone_zaliczki", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rozliczone_zaliczki_faktury_sprzedazy_FakturaId",
                        column: x => x.FakturaId,
                        principalTable: "faktury_sprzedazy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pozycje_zamowien_FakturaId",
                table: "pozycje_zamowien",
                column: "FakturaId");

            migrationBuilder.CreateIndex(
                name: "IX_pozycje_zamowien_FirmaId_FakturaId",
                table: "pozycje_zamowien",
                columns: new[] { "FirmaId", "FakturaId" });

            migrationBuilder.CreateIndex(
                name: "IX_rozliczone_zaliczki_FakturaId",
                table: "rozliczone_zaliczki",
                column: "FakturaId");

            migrationBuilder.CreateIndex(
                name: "IX_rozliczone_zaliczki_FirmaId_ZaliczkowaId",
                table: "rozliczone_zaliczki",
                columns: new[] { "FirmaId", "ZaliczkowaId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pozycje_zamowien");

            migrationBuilder.DropTable(
                name: "rozliczone_zaliczki");
        }
    }
}
