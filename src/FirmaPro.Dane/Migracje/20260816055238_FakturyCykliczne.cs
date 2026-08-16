using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class FakturyCykliczne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wzorce_cykliczne",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nazwa = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    KontrahentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rytm = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DzienMiesiaca = table.Column<int>(type: "integer", nullable: false),
                    TerminPlatnosciDni = table.Column<int>(type: "integer", nullable: false),
                    FormaPlatnosci = table.Column<int>(type: "integer", nullable: true),
                    Od = table.Column<DateOnly>(type: "date", nullable: false),
                    Do = table.Column<DateOnly>(type: "date", nullable: true),
                    NastepneWystawienie = table.Column<DateOnly>(type: "date", nullable: false),
                    OstatnieWystawienie = table.Column<DateOnly>(type: "date", nullable: true),
                    Aktywny = table.Column<bool>(type: "boolean", nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wzorce_cykliczne", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wzorce_cykliczne_kontrahenci_KontrahentId",
                        column: x => x.KontrahentId,
                        principalTable: "kontrahenci",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pozycje_wzorcow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    WzorzecId = table.Column<Guid>(type: "uuid", nullable: false),
                    NrWiersza = table.Column<int>(type: "integer", nullable: false),
                    Nazwa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Jednostka = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Ilosc = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CenaNetto = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    KodStawki = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Gtu = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pozycje_wzorcow", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pozycje_wzorcow_wzorce_cykliczne_WzorzecId",
                        column: x => x.WzorzecId,
                        principalTable: "wzorce_cykliczne",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pozycje_wzorcow_WzorzecId",
                table: "pozycje_wzorcow",
                column: "WzorzecId");

            migrationBuilder.CreateIndex(
                name: "IX_wzorce_cykliczne_FirmaId_Aktywny_NastepneWystawienie",
                table: "wzorce_cykliczne",
                columns: new[] { "FirmaId", "Aktywny", "NastepneWystawienie" });

            migrationBuilder.CreateIndex(
                name: "IX_wzorce_cykliczne_KontrahentId",
                table: "wzorce_cykliczne",
                column: "KontrahentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pozycje_wzorcow");

            migrationBuilder.DropTable(
                name: "wzorce_cykliczne");
        }
    }
}
