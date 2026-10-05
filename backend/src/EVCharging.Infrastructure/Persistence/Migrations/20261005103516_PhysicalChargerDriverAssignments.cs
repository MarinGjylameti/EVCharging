using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhysicalChargerDriverAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_claim_active_driver",
                table: "charging_connection_requests");

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_driver_id",
                table: "ocpp_charge_points",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "driver_assignment_since",
                table: "ocpp_charge_points",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_card_id_tag",
                table: "ocpp_charge_points",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_automatic",
                table: "charging_connection_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_charge_points_assigned_driver_id",
                table: "ocpp_charge_points",
                column: "assigned_driver_id");

            migrationBuilder.CreateIndex(
                name: "ux_claim_active_driver",
                table: "charging_connection_requests",
                column: "driver_id",
                unique: true,
                filter: "state IN ('Waiting', 'Linked') AND NOT is_automatic");

            migrationBuilder.AddForeignKey(
                name: "FK_ocpp_charge_points_accounts_assigned_driver_id",
                table: "ocpp_charge_points",
                column: "assigned_driver_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ocpp_charge_points_accounts_assigned_driver_id",
                table: "ocpp_charge_points");

            migrationBuilder.DropIndex(
                name: "IX_ocpp_charge_points_assigned_driver_id",
                table: "ocpp_charge_points");

            migrationBuilder.DropIndex(
                name: "ux_claim_active_driver",
                table: "charging_connection_requests");

            migrationBuilder.DropColumn(
                name: "assigned_driver_id",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "driver_assignment_since",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "driver_card_id_tag",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "is_automatic",
                table: "charging_connection_requests");

            migrationBuilder.CreateIndex(
                name: "ux_claim_active_driver",
                table: "charging_connection_requests",
                column: "driver_id",
                unique: true,
                filter: "state IN ('Waiting', 'Linked')");
        }
    }
}
