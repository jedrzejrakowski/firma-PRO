using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class SrodkiTrwale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "srodki_trwale",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nazwa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NumerInwentarzowy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DataPrzyjecia = table.Column<DateOnly>(type: "date", nullable: false),
                    WartoscPoczatkowa = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Metoda = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    StawkaRoczna = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    Wspolczynnik = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    LimitKosztu = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DataLikwidacji = table.Column<DateOnly>(type: "date", nullable: true),
                    Uwagi = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_srodki_trwale", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_srodki_trwale_FirmaId_NumerInwentarzowy",
                table: "srodki_trwale",
                columns: new[] { "FirmaId", "NumerInwentarzowy" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "srodki_trwale");
        }
    }
}
