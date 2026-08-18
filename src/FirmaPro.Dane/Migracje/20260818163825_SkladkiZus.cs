using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class SkladkiZus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "skladki_zus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rok = table.Column<int>(type: "integer", nullable: false),
                    Miesiac = table.Column<int>(type: "integer", nullable: false),
                    Emerytalna = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Rentowa = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Chorobowe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Wypadkowa = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FunduszPracy = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Zdrowotna = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PodstawaSpolecznych = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PodstawaZdrowotnej = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DataZaplaty = table.Column<DateOnly>(type: "date", nullable: true),
                    SpoleczneWKosztach = table.Column<bool>(type: "boolean", nullable: false),
                    Uwagi = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skladki_zus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ustawienia_zus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tytul = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Chorobowe = table.Column<bool>(type: "boolean", nullable: false),
                    StopaWypadkowa = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: false),
                    BezFunduszuPracy = table.Column<bool>(type: "boolean", nullable: false),
                    DataRozpoczecia = table.Column<DateOnly>(type: "date", nullable: true),
                    SpoleczneWKosztach = table.Column<bool>(type: "boolean", nullable: false),
                    DochodPoprzedniegoRoku = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PrzychodPoprzedniegoRoku = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DniProwadzeniaPoprzedniegoRoku = table.Column<int>(type: "integer", nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ustawienia_zus", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_skladki_zus_FirmaId_Rok_Miesiac",
                table: "skladki_zus",
                columns: new[] { "FirmaId", "Rok", "Miesiac" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ustawienia_zus_FirmaId",
                table: "ustawienia_zus",
                column: "FirmaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "skladki_zus");

            migrationBuilder.DropTable(
                name: "ustawienia_zus");
        }
    }
}
