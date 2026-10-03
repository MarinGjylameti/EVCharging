using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BatteryCapacityForChargingEstimates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "battery_capacity_kwh",
                table: "charging_sessions",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "soc_received_at",
                table: "charging_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "battery_capacity_kwh",
                table: "accounts",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_session_battery_capacity",
                table: "charging_sessions",
                sql: "battery_capacity_kwh IS NULL OR battery_capacity_kwh > 0 AND battery_capacity_kwh <= 1000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_battery_capacity",
                table: "accounts",
                sql: "battery_capacity_kwh IS NULL OR battery_capacity_kwh > 0 AND battery_capacity_kwh <= 1000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_session_battery_capacity",
                table: "charging_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_battery_capacity",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "battery_capacity_kwh",
                table: "charging_sessions");

            migrationBuilder.DropColumn(
                name: "soc_received_at",
                table: "charging_sessions");

            migrationBuilder.DropColumn(
                name: "battery_capacity_kwh",
                table: "accounts");
        }
    }
}
