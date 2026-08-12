using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class Platnosci : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Rodzaj",
                table: "wysylki_faktur",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "platnosci",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FakturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kwota = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Uwagi = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platnosci", x => x.Id);
                    table.ForeignKey(
                        name: "FK_platnosci_faktury_sprzedazy_FakturaId",
                        column: x => x.FakturaId,
                        principalTable: "faktury_sprzedazy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_platnosci_FakturaId",
                table: "platnosci",
                column: "FakturaId");

            migrationBuilder.CreateIndex(
                name: "IX_platnosci_FirmaId_FakturaId",
                table: "platnosci",
                columns: new[] { "FirmaId", "FakturaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platnosci");

            migrationBuilder.DropColumn(
                name: "Rodzaj",
                table: "wysylki_faktur");
        }
    }
}
