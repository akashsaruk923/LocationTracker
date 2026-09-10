using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocationTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVillageAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "LocationPings",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "LocationPings",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Postcode",
                table: "LocationPings",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Village",
                table: "LocationPings",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "LocationPings");

            migrationBuilder.DropColumn(
                name: "District",
                table: "LocationPings");

            migrationBuilder.DropColumn(
                name: "Postcode",
                table: "LocationPings");

            migrationBuilder.DropColumn(
                name: "Village",
                table: "LocationPings");
        }
    }
}
