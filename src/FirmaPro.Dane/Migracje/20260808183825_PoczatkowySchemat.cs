using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class PoczatkowySchemat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "firmy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nazwa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Nip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    KodKraju = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    AdresLinia1 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AdresLinia2 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Telefon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RachunekBankowy = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    NazwaBanku = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MiejsceWystawienia = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StopkaFaktury = table.Column<string>(type: "character varying(3500)", maxLength: 3500, nullable: true),
                    DomyslnyTerminPlatnosciDni = table.Column<int>(type: "integer", nullable: false),
                    Srodowisko = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TokenKsefZaszyfrowany = table.Column<byte[]>(type: "bytea", nullable: true),
                    Aktywna = table.Column<bool>(type: "boolean", nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firmy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "kontrahenci",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nazwa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Nip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    KodKraju = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    AdresLinia1 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AdresLinia2 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Telefon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    KodUe = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    NrVatUe = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    JednostkaPodrzednaJst = table.Column<bool>(type: "boolean", nullable: false),
                    CzlonekGrupyVat = table.Column<bool>(type: "boolean", nullable: false),
                    Aktywny = table.Column<bool>(type: "boolean", nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kontrahenci", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "serie_numeracji",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nazwa = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Wzor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Rok = table.Column<int>(type: "integer", nullable: false),
                    Miesiac = table.Column<int>(type: "integer", nullable: false),
                    OstatniNumer = table.Column<int>(type: "integer", nullable: false),
                    Domyslna = table.Column<bool>(type: "boolean", nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_serie_numeracji", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "uzytkownicy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    HaszHasla = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ImieINazwisko = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Aktywny = table.Column<bool>(type: "boolean", nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_uzytkownicy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "faktury_sprzedazy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DataWystawienia = table.Column<DateOnly>(type: "date", nullable: false),
                    DataSprzedazy = table.Column<DateOnly>(type: "date", nullable: true),
                    MiejsceWystawienia = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Waluta = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rodzaj = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    KontrahentId = table.Column<Guid>(type: "uuid", nullable: false),
                    NabywcaNazwa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NabywcaNip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    NabywcaKodKraju = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    NabywcaAdresLinia1 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NabywcaAdresLinia2 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    NabywcaKodUe = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    NabywcaNrVatUe = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    NabywcaJst = table.Column<bool>(type: "boolean", nullable: false),
                    NabywcaGrupaVat = table.Column<bool>(type: "boolean", nullable: false),
                    FormaPlatnosci = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    TerminPlatnosci = table.Column<DateOnly>(type: "date", nullable: true),
                    RachunekBankowy = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    NazwaBanku = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Zaplacono = table.Column<bool>(type: "boolean", nullable: false),
                    DataZaplaty = table.Column<DateOnly>(type: "date", nullable: true),
                    Stopka = table.Column<string>(type: "character varying(3500)", maxLength: 3500, nullable: true),
                    PodstawaZwolnienia = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RazemNetto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RazemVat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RazemBrutto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NumerKsef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DataPrzyjeciaKsef = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UwagiKsef = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SkrotXml = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faktury_sprzedazy", x => x.Id);
                    table.ForeignKey(
                        name: "FK_faktury_sprzedazy_firmy_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "firmy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_faktury_sprzedazy_kontrahenci_KontrahentId",
                        column: x => x.KontrahentId,
                        principalTable: "kontrahenci",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "czlonkostwa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UzytkownikId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rola = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_czlonkostwa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_czlonkostwa_firmy_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "firmy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_czlonkostwa_uzytkownicy_UzytkownikId",
                        column: x => x.UzytkownikId,
                        principalTable: "uzytkownicy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pozycje_faktur_sprzedazy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FakturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    NrWiersza = table.Column<int>(type: "integer", nullable: false),
                    Nazwa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Jednostka = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Ilosc = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CenaNetto = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    KodStawki = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Gtu = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Pkwiu = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Cn = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Indeks = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WartoscNetto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    KwotaVat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pozycje_faktur_sprzedazy", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pozycje_faktur_sprzedazy_faktury_sprzedazy_FakturaId",
                        column: x => x.FakturaId,
                        principalTable: "faktury_sprzedazy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_czlonkostwa_FirmaId",
                table: "czlonkostwa",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_czlonkostwa_UzytkownikId_FirmaId",
                table: "czlonkostwa",
                columns: new[] { "UzytkownikId", "FirmaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_faktury_sprzedazy_FirmaId_DataWystawienia",
                table: "faktury_sprzedazy",
                columns: new[] { "FirmaId", "DataWystawienia" });

            migrationBuilder.CreateIndex(
                name: "IX_faktury_sprzedazy_FirmaId_Numer",
                table: "faktury_sprzedazy",
                columns: new[] { "FirmaId", "Numer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_faktury_sprzedazy_KontrahentId",
                table: "faktury_sprzedazy",
                column: "KontrahentId");

            migrationBuilder.CreateIndex(
                name: "IX_faktury_sprzedazy_NumerKsef",
                table: "faktury_sprzedazy",
                column: "NumerKsef");

            migrationBuilder.CreateIndex(
                name: "IX_firmy_Nip",
                table: "firmy",
                column: "Nip");

            migrationBuilder.CreateIndex(
                name: "IX_kontrahenci_FirmaId_Nazwa",
                table: "kontrahenci",
                columns: new[] { "FirmaId", "Nazwa" });

            migrationBuilder.CreateIndex(
                name: "IX_kontrahenci_FirmaId_Nip",
                table: "kontrahenci",
                columns: new[] { "FirmaId", "Nip" });

            migrationBuilder.CreateIndex(
                name: "IX_pozycje_faktur_sprzedazy_FakturaId_NrWiersza",
                table: "pozycje_faktur_sprzedazy",
                columns: new[] { "FakturaId", "NrWiersza" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_serie_numeracji_FirmaId_Nazwa_Rok_Miesiac",
                table: "serie_numeracji",
                columns: new[] { "FirmaId", "Nazwa", "Rok", "Miesiac" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_uzytkownicy_Email",
                table: "uzytkownicy",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "czlonkostwa");

            migrationBuilder.DropTable(
                name: "pozycje_faktur_sprzedazy");

            migrationBuilder.DropTable(
                name: "serie_numeracji");

            migrationBuilder.DropTable(
                name: "uzytkownicy");

            migrationBuilder.DropTable(
                name: "faktury_sprzedazy");

            migrationBuilder.DropTable(
                name: "firmy");

            migrationBuilder.DropTable(
                name: "kontrahenci");
        }
    }
}
