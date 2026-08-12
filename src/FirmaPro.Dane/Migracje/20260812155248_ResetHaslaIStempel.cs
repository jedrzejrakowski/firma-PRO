using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class ResetHaslaIStempel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StempelBezpieczenstwa",
                table: "uzytkownicy",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "resety_hasla",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UzytkownikId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kod = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WaznoscDoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WykorzystanoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UtworzonoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ZmienionoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resety_hasla", x => x.Id);
                    table.ForeignKey(
                        name: "FK_resety_hasla_uzytkownicy_UzytkownikId",
                        column: x => x.UzytkownikId,
                        principalTable: "uzytkownicy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_resety_hasla_Kod",
                table: "resety_hasla",
                column: "Kod",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resety_hasla_UzytkownikId",
                table: "resety_hasla",
                column: "UzytkownikId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resety_hasla");

            migrationBuilder.DropColumn(
                name: "StempelBezpieczenstwa",
                table: "uzytkownicy");
        }
    }
}
