using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocationTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocationPings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false, defaultValue: "gps"),
                    ClientId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    GoogleMapsUrl = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AccuracyMeters = table.Column<double>(type: "float", nullable: true),
                    AltitudeMeters = table.Column<double>(type: "float", nullable: true),
                    AltitudeAccuracyMeters = table.Column<double>(type: "float", nullable: true),
                    Heading = table.Column<double>(type: "float", nullable: true),
                    SpeedMetersPerSecond = table.Column<double>(type: "float", nullable: true),
                    DeviceTimestampUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeviceTimestampIst = table.Column<DateTime>(type: "datetime2", nullable: true),
                    City = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Region = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtIst = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationPings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocationPings_ClientId",
                table: "LocationPings",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationPings_CreatedAtIst",
                table: "LocationPings",
                column: "CreatedAtIst");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocationPings");
        }
    }
}
