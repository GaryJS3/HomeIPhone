using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeIPhone.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceProtocol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Configuration_DeviceProtocol",
                table: "Phones",
                type: "TEXT",
                nullable: false,
                defaultValue: "SCCP");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Configuration_DeviceProtocol",
                table: "Phones");
        }
    }
}
