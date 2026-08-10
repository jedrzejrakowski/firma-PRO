using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class IndeksNumeruKsefZakupu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_faktury_zakupu_FirmaId_NumerKsef",
                table: "faktury_zakupu",
                columns: new[] { "FirmaId", "NumerKsef" },
                unique: true,
                filter: "\"NumerKsef\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_faktury_zakupu_FirmaId_NumerKsef",
                table: "faktury_zakupu");
        }
    }
}
