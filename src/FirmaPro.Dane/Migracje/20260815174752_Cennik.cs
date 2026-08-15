using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class Cennik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cennik",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nazwa = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Jednostka = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CenaNetto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    KodStawki = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Gtu = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Pkwiu = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Cn = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Indeks = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Aktywna = table.Column<bool>(type: "boolean", nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cennik", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cennik_FirmaId_Aktywna_Nazwa",
                table: "cennik",
                columns: new[] { "FirmaId", "Aktywna", "Nazwa" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cennik");
        }
    }
}
