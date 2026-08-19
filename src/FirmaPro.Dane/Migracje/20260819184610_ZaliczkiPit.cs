using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class ZaliczkiPit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StrataDoOdliczenia",
                table: "firmy",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ZaliczkiKwartalne",
                table: "firmy",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "zaliczki_pit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rok = table.Column<int>(type: "integer", nullable: false),
                    Numer = table.Column<int>(type: "integer", nullable: false),
                    Kwartalna = table.Column<bool>(type: "boolean", nullable: false),
                    Kwota = table.Column<long>(type: "bigint", nullable: false),
                    DataZaplaty = table.Column<DateOnly>(type: "date", nullable: false),
                    Uwagi = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zaliczki_pit", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_zaliczki_pit_FirmaId_Rok_Kwartalna_Numer",
                table: "zaliczki_pit",
                columns: new[] { "FirmaId", "Rok", "Kwartalna", "Numer" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "zaliczki_pit");

            migrationBuilder.DropColumn(
                name: "StrataDoOdliczenia",
                table: "firmy");

            migrationBuilder.DropColumn(
                name: "ZaliczkiKwartalne",
                table: "firmy");
        }
    }
}
