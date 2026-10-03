using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChargerFaultDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "command_version",
                table: "ocpp_inbound_messages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "error_info",
                table: "ocpp_connectors",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "command_version",
                table: "ocpp_charge_points",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "error_code",
                table: "ocpp_charge_points",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error_info",
                table: "ocpp_charge_points",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reported_status",
                table: "ocpp_charge_points",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "status_observed_at",
                table: "ocpp_charge_points",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "warning_code",
                table: "charging_stations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "completion_warning",
                table: "charging_sessions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "latest_readings_json",
                table: "charging_sessions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stop_reason",
                table: "charging_sessions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "device_fault_code",
                table: "charging_connectors",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "device_fault_info",
                table: "charging_connectors",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "device_status",
                table: "charging_connectors",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fault_code",
                table: "charging_connectors",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fault_info",
                table: "charging_connectors",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE charging_sessions AS business
                SET stop_reason = physical.stop_reason
                FROM ocpp_charging_transactions AS physical
                WHERE physical.session_id = business.id AND physical.stop_reason IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "command_version",
                table: "ocpp_inbound_messages");

            migrationBuilder.DropColumn(
                name: "error_info",
                table: "ocpp_connectors");

            migrationBuilder.DropColumn(
                name: "command_version",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "error_code",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "error_info",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "reported_status",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "status_observed_at",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "warning_code",
                table: "charging_stations");

            migrationBuilder.DropColumn(
                name: "completion_warning",
                table: "charging_sessions");

            migrationBuilder.DropColumn(
                name: "latest_readings_json",
                table: "charging_sessions");

            migrationBuilder.DropColumn(
                name: "stop_reason",
                table: "charging_sessions");

            migrationBuilder.DropColumn(
                name: "device_fault_code",
                table: "charging_connectors");

            migrationBuilder.DropColumn(
                name: "device_fault_info",
                table: "charging_connectors");

            migrationBuilder.DropColumn(
                name: "device_status",
                table: "charging_connectors");

            migrationBuilder.DropColumn(
                name: "fault_code",
                table: "charging_connectors");

            migrationBuilder.DropColumn(
                name: "fault_info",
                table: "charging_connectors");
        }
    }
}
