using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirmaPro.Dane.Migracje
{
    /// <inheritdoc />
    public partial class XmlWyslany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "XmlWyslany",
                table: "faktury_sprzedazy",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "XmlWyslany",
                table: "faktury_sprzedazy");
        }
    }
}
