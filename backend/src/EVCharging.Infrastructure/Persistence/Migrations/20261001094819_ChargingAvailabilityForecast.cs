using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChargingAvailabilityForecast : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "forecast_available_at",
                table: "charging_stations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "forecast_connector_id",
                table: "charging_stations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "forecast_observed_at",
                table: "charging_stations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "forecast_source",
                table: "charging_stations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "forecast_available_at",
                table: "charging_stations");

            migrationBuilder.DropColumn(
                name: "forecast_connector_id",
                table: "charging_stations");

            migrationBuilder.DropColumn(
                name: "forecast_observed_at",
                table: "charging_stations");

            migrationBuilder.DropColumn(
                name: "forecast_source",
                table: "charging_stations");
        }
    }
}
