using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class PodmiotyInne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "podmioty_inne_faktur",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FakturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    NrKolejny = table.Column<int>(type: "integer", nullable: false),
                    Nazwa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Nip = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    KodKraju = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    AdresLinia1 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AdresLinia2 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Telefon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Rola = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OpisRoli = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Udzial = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    NrKlienta = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_podmioty_inne_faktur", x => x.Id);
                    table.ForeignKey(
                        name: "FK_podmioty_inne_faktur_faktury_sprzedazy_FakturaId",
                        column: x => x.FakturaId,
                        principalTable: "faktury_sprzedazy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_podmioty_inne_faktur_FakturaId",
                table: "podmioty_inne_faktur",
                column: "FakturaId");

            migrationBuilder.CreateIndex(
                name: "IX_podmioty_inne_faktur_FirmaId_FakturaId",
                table: "podmioty_inne_faktur",
                columns: new[] { "FirmaId", "FakturaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "podmioty_inne_faktur");
        }
    }
}
