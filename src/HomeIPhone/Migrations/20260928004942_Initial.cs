using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeIPhone.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Discovered",
                columns: table => new
                {
                    MacAddress = table.Column<string>(type: "TEXT", nullable: false),
                    LastKnownIp = table.Column<string>(type: "TEXT", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastRequestedFile = table.Column<string>(type: "TEXT", nullable: false),
                    RequestCount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Discovered", x => x.MacAddress);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MacAddress = table.Column<string>(type: "TEXT", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Phones",
                columns: table => new
                {
                    MacAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_FriendlyName = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_Description = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_Location = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_Model = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_ServicesUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_DirectoryUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_IdleUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_InformationUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_MessagesUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_NtpServer = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_TimeZone = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_DateTemplate = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_WebAccess = table.Column<bool>(type: "INTEGER", nullable: false),
                    Configuration_SshAccess = table.Column<bool>(type: "INTEGER", nullable: false),
                    Configuration_FirmwareLoad = table.Column<string>(type: "TEXT", nullable: false),
                    Configuration_RawOverrideXml = table.Column<string>(type: "TEXT", nullable: false),
                    LastKnownIp = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastHttpSuccessUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastHttpFailureUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastTftpUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastConfigRequested = table.Column<string>(type: "TEXT", nullable: true),
                    Online = table.Column<bool>(type: "INTEGER", nullable: false),
                    Firmware = table.Column<string>(type: "TEXT", nullable: true),
                    ConfigVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfigHash = table.Column<string>(type: "TEXT", nullable: false),
                    LastServedConfigVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    LastConfigServedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Phones", x => x.MacAddress);
                });

            migrationBuilder.CreateTable(
                name: "TftpEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MacAddress = table.Column<string>(type: "TEXT", nullable: true),
                    SourceIp = table.Column<string>(type: "TEXT", nullable: false),
                    Filename = table.Column<string>(type: "TEXT", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: false),
                    BytesTransferred = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftpEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_MacAddress_TimestampUtc",
                table: "Events",
                columns: new[] { "MacAddress", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TftpEvents_MacAddress_TimestampUtc",
                table: "TftpEvents",
                columns: new[] { "MacAddress", "TimestampUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Discovered");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Phones");

            migrationBuilder.DropTable(
                name: "TftpEvents");
        }
    }
}
