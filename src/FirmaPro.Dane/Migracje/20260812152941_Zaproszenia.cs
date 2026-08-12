using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class Zaproszenia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "zaproszenia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Rola = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kod = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WaznoscDoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PrzyjeteUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ZapraszajacyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zaproszenia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_zaproszenia_firmy_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "firmy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_zaproszenia_FirmaId_Email",
                table: "zaproszenia",
                columns: new[] { "FirmaId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_zaproszenia_Kod",
                table: "zaproszenia",
                column: "Kod",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "zaproszenia");
        }
    }
}
