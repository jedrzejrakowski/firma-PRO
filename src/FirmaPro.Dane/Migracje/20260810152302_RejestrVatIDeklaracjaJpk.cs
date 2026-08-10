using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class RejestrVatIDeklaracjaJpk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KodUrzeduSkarbowego",
                table: "firmy",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TypOkresuVat",
                table: "firmy",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "DataUjeciaVat",
                table: "faktury_sprzedazy",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateTable(
                name: "faktury_zakupu",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DataWystawienia = table.Column<DateOnly>(type: "date", nullable: false),
                    DataWplywu = table.Column<DateOnly>(type: "date", nullable: false),
                    DataObowiazkuPodatkowego = table.Column<DateOnly>(type: "date", nullable: false),
                    DataUjecia = table.Column<DateOnly>(type: "date", nullable: false),
                    KontrahentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SprzedawcaNazwa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SprzedawcaNip = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Waluta = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rodzaj = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Odliczany = table.Column<bool>(type: "boolean", nullable: false),
                    RazemNetto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RazemVat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RazemBrutto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NumerKsef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Uwagi = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faktury_zakupu", x => x.Id);
                    table.ForeignKey(
                        name: "FK_faktury_zakupu_firmy_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "firmy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_faktury_zakupu_kontrahenci_KontrahentId",
                        column: x => x.KontrahentId,
                        principalTable: "kontrahenci",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "zamkniecia_okresow_vat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rok = table.Column<int>(type: "integer", nullable: false),
                    Numer = table.Column<int>(type: "integer", nullable: false),
                    Typ = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NadwyzkaDoPrzeniesienia = table.Column<long>(type: "bigint", nullable: false),
                    PodatekDoWplaty = table.Column<long>(type: "bigint", nullable: false),
                    DataZamkniecia = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zamkniecia_okresow_vat", x => x.Id);
                    table.ForeignKey(
                        name: "FK_zamkniecia_okresow_vat_firmy_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "firmy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "kwoty_vat_zakupu",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FakturaZakupuId = table.Column<Guid>(type: "uuid", nullable: false),
                    KodStawki = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Netto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kwoty_vat_zakupu", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kwoty_vat_zakupu_faktury_zakupu_FakturaZakupuId",
                        column: x => x.FakturaZakupuId,
                        principalTable: "faktury_zakupu",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_faktury_sprzedazy_FirmaId_DataUjeciaVat",
                table: "faktury_sprzedazy",
                columns: new[] { "FirmaId", "DataUjeciaVat" });

            migrationBuilder.CreateIndex(
                name: "IX_faktury_zakupu_FirmaId_DataUjecia",
                table: "faktury_zakupu",
                columns: new[] { "FirmaId", "DataUjecia" });

            migrationBuilder.CreateIndex(
                name: "IX_faktury_zakupu_FirmaId_SprzedawcaNip_Numer",
                table: "faktury_zakupu",
                columns: new[] { "FirmaId", "SprzedawcaNip", "Numer" },
                unique: true,
                filter: "\"SprzedawcaNip\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_faktury_zakupu_KontrahentId",
                table: "faktury_zakupu",
                column: "KontrahentId");

            migrationBuilder.CreateIndex(
                name: "IX_kwoty_vat_zakupu_FakturaZakupuId_KodStawki",
                table: "kwoty_vat_zakupu",
                columns: new[] { "FakturaZakupuId", "KodStawki" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_zamkniecia_okresow_vat_FirmaId_Typ_Rok_Numer",
                table: "zamkniecia_okresow_vat",
                columns: new[] { "FirmaId", "Typ", "Rok", "Numer" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kwoty_vat_zakupu");

            migrationBuilder.DropTable(
                name: "zamkniecia_okresow_vat");

            migrationBuilder.DropTable(
                name: "faktury_zakupu");

            migrationBuilder.DropIndex(
                name: "IX_faktury_sprzedazy_FirmaId_DataUjeciaVat",
                table: "faktury_sprzedazy");

            migrationBuilder.DropColumn(
                name: "KodUrzeduSkarbowego",
                table: "firmy");

            migrationBuilder.DropColumn(
                name: "TypOkresuVat",
                table: "firmy");

            migrationBuilder.DropColumn(
                name: "DataUjeciaVat",
                table: "faktury_sprzedazy");
        }
    }
}
