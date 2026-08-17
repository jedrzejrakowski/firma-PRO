using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class SpisZNatury : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "spisy_z_natury",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Uwagi = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Zamkniety = table.Column<bool>(type: "boolean", nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spisy_z_natury", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pozycje_spisow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpisId = table.Column<Guid>(type: "uuid", nullable: false),
                    NrPozycji = table.Column<int>(type: "integer", nullable: false),
                    Nazwa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Jednostka = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ilosc = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CenaJednostkowa = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Wycena = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pozycje_spisow", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pozycje_spisow_spisy_z_natury_SpisId",
                        column: x => x.SpisId,
                        principalTable: "spisy_z_natury",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pozycje_spisow_FirmaId_SpisId",
                table: "pozycje_spisow",
                columns: new[] { "FirmaId", "SpisId" });

            migrationBuilder.CreateIndex(
                name: "IX_pozycje_spisow_SpisId",
                table: "pozycje_spisow",
                column: "SpisId");

            migrationBuilder.CreateIndex(
                name: "IX_spisy_z_natury_FirmaId_Data",
                table: "spisy_z_natury",
                columns: new[] { "FirmaId", "Data" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pozycje_spisow");

            migrationBuilder.DropTable(
                name: "spisy_z_natury");
        }
    }
}
