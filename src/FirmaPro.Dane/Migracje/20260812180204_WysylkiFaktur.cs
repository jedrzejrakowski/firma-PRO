using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class WysylkiFaktur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wysylki_faktur",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FakturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Adres = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UzytkownikId = table.Column<Guid>(type: "uuid", nullable: true),
                    WyslanoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZXmlem = table.Column<bool>(type: "boolean", nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wysylki_faktur", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wysylki_faktur_faktury_sprzedazy_FakturaId",
                        column: x => x.FakturaId,
                        principalTable: "faktury_sprzedazy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wysylki_faktur_FakturaId",
                table: "wysylki_faktur",
                column: "FakturaId");

            migrationBuilder.CreateIndex(
                name: "IX_wysylki_faktur_FirmaId_FakturaId",
                table: "wysylki_faktur",
                columns: new[] { "FirmaId", "FakturaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wysylki_faktur");
        }
    }
}
