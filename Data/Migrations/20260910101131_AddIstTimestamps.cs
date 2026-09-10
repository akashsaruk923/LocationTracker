using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocationTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIstTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtIst",
                table: "LocationPings",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeviceTimestampIst",
                table: "LocationPings",
                type: "timestamp without time zone",
                nullable: true);

            // Backfill existing rows from their UTC counterparts (IST = UTC+5:30).
            migrationBuilder.Sql(
                "UPDATE \"LocationPings\" SET \"CreatedAtIst\" = \"CreatedAtUtc\" AT TIME ZONE 'Asia/Kolkata';");
            migrationBuilder.Sql(
                "UPDATE \"LocationPings\" SET \"DeviceTimestampIst\" = \"DeviceTimestampUtc\" AT TIME ZONE 'Asia/Kolkata' WHERE \"DeviceTimestampUtc\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtIst",
                table: "LocationPings");

            migrationBuilder.DropColumn(
                name: "DeviceTimestampIst",
                table: "LocationPings");
        }
    }
}
