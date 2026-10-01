using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverChargingConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charging_connection_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_number = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charging_connection_requests", x => x.id);
                    table.CheckConstraint("ck_connection_link", "(state IN ('Linked', 'Completed')) = (session_id IS NOT NULL AND linked_at IS NOT NULL)");
                    table.CheckConstraint("ck_connection_state", "state IN ('Waiting', 'Linked', 'Completed', 'Cancelled', 'Expired')");
                    table.CheckConstraint("ck_connection_time", "expires_at > created_at AND connector_number > 0");
                    table.ForeignKey(
                        name: "FK_charging_connection_requests_accounts_driver_id",
                        column: x => x.driver_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_charging_connection_requests_charging_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "charging_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_charging_connection_requests_ocpp_charging_transactions_ses~",
                        column: x => x.session_id,
                        principalTable: "ocpp_charging_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_charging_connection_requests_driver_id_created_at",
                table: "charging_connection_requests",
                columns: new[] { "driver_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_charging_connection_requests_session_id",
                table: "charging_connection_requests",
                column: "session_id",
                unique: true,
                filter: "session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_charging_connection_requests_station_id_connector_number",
                table: "charging_connection_requests",
                columns: new[] { "station_id", "connector_number" },
                unique: true,
                filter: "state = 'Waiting'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charging_connection_requests");
        }
    }
}
