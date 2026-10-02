using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChargingBusinessSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_charging_connection_requests_ocpp_charging_transactions_ses~",
                table: "charging_connection_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_ocpp_charging_transactions_accounts_driver_id",
                table: "ocpp_charging_transactions");

            migrationBuilder.DropIndex(
                name: "IX_ocpp_charging_transactions_driver_id_started_at",
                table: "ocpp_charging_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ocpp_transaction_meter",
                table: "ocpp_charging_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ocpp_transaction_price",
                table: "ocpp_charging_transactions");

            migrationBuilder.DropIndex(
                name: "IX_charging_connection_requests_station_id_connector_number",
                table: "charging_connection_requests");

            migrationBuilder.AddColumn<Guid>(name: "session_id", table: "ocpp_charging_transactions", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(
                name: "business_connector_id",
                table: "ocpp_connectors",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "charging_connectors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    is_online = table.Column<bool>(type: "boolean", nullable: false),
                    status_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charging_connectors", x => x.id);
                    table.UniqueConstraint("AK_charging_connectors_id_station_id_number", x => new { x.id, x.station_id, x.number });
                    table.CheckConstraint("ck_charging_connector_number", "number > 0");
                    table.ForeignKey(
                        name: "FK_charging_connectors_charging_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "charging_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "charging_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_number = table.Column<int>(type: "integer", nullable: false),
                    unit_price_snapshot = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    start_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stop_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    energy_kwh = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    power_kw = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    state_of_charge = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true),
                    temperature_celsius = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: true),
                    last_measurement_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_measurement_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    power_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    power_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    soc_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    temperature_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    energy_scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    power_scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charging_sessions", x => x.id);
                    table.UniqueConstraint("AK_charging_sessions_id_point_id_connector_number", x => new { x.id, x.point_id, x.connector_number });
                    table.CheckConstraint("ck_session_time", "ended_at IS NULL OR ended_at >= started_at");
                    table.CheckConstraint("ck_session_values", "(energy_kwh IS NULL OR energy_kwh >= 0) AND unit_price_snapshot >= 0 AND (state_of_charge IS NULL OR state_of_charge BETWEEN 0 AND 100)");
                    table.ForeignKey(
                        name: "FK_charging_sessions_charging_connectors_connector_id_point_id~",
                        columns: x => new { x.connector_id, x.point_id, x.connector_number },
                        principalTable: "charging_connectors",
                        principalColumns: new[] { "id", "station_id", "number" },
                        onDelete: ReferentialAction.Restrict);
                });

            // Preserve existing physical sessions before retiring duplicated business columns.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM ocpp_charging_transactions t WHERE t.station_id IS NULL OR t.unit_price_snapshot IS NULL
                    OR NOT EXISTS (SELECT 1 FROM ocpp_connectors c WHERE c.charge_point_id=t.charge_point_id
                      AND c.connector_number=t.connector_number AND c.station_id=t.station_id)) THEN
                    RAISE EXCEPTION 'Charging migration requires every legacy transaction to have a mapped station and original tariff; reconcile these records before retrying.';
                  END IF;
                END $$;
                INSERT INTO charging_connectors (id, station_id, number, status, is_online, status_observed_at, status_received_at, version)
                  SELECT id, station_id, connector_number,
                    CASE reported_status WHEN 'Preparing' THEN 'Connecting' WHEN 'SuspendedEV' THEN 'PausedByVehicle'
                      WHEN 'SuspendedEVSE' THEN 'PausedByCharger' ELSE COALESCE(reported_status, 'Unknown') END,
                    false, status_observed_at, status_received_at, version FROM ocpp_connectors WHERE station_id IS NOT NULL;
                UPDATE ocpp_connectors SET business_connector_id=id WHERE station_id IS NOT NULL;
                INSERT INTO charging_sessions (id, point_id, connector_id, connector_number, unit_price_snapshot, currency,
                    started_at, start_received_at, ended_at, stop_received_at, energy_kwh, power_kw,
                    last_measurement_at, last_measurement_received_at, energy_scope, power_scope, status, version, is_demo,
                    state_of_charge, temperature_celsius, power_observed_at, power_received_at, soc_observed_at, temperature_observed_at)
                  SELECT t.id, t.station_id, c.id, t.connector_number, t.unit_price_snapshot, COALESCE(t.currency, s.currency),
                    t.started_at, t.start_received_at, t.stopped_at, t.stop_received_at, t.energy_kwh, t.current_power_kw,
                    COALESCE(t.last_measurement_at, t.started_at), m.received_at,
                    CASE WHEN t.stopped_at IS NOT NULL THEN 'Total' ELSE e.scope END, p.scope,
                    CASE WHEN t.stopped_at IS NOT NULL THEN 'Completed' ELSE b.status END, 1, s.is_demo,
                    soc.numeric_value, temp.numeric_value, p.observed_at, p.received_at, soc.observed_at, temp.observed_at
                  FROM ocpp_charging_transactions t
                  JOIN ocpp_connectors c ON c.charge_point_id=t.charge_point_id AND c.connector_number=t.connector_number
                  JOIN charging_connectors b ON b.id=c.id JOIN charging_stations s ON s.id=t.station_id
                  LEFT JOIN LATERAL (SELECT max(received_at) received_at FROM ocpp_meter_samples WHERE transaction_id=t.id) m ON true
                  LEFT JOIN LATERAL (SELECT COALESCE(phase,'Total') scope FROM ocpp_meter_samples WHERE transaction_id=t.id
                    AND measurand='Energy.Active.Import.Register' AND format IS DISTINCT FROM 'SignedData' AND numeric_value IS NOT NULL
                    ORDER BY observed_at DESC, received_at DESC LIMIT 1) e ON true
                  LEFT JOIN LATERAL (SELECT COALESCE(phase,'Total') scope, observed_at, received_at FROM ocpp_meter_samples WHERE transaction_id=t.id
                    AND measurand='Power.Active.Import' AND format IS DISTINCT FROM 'SignedData' AND numeric_value IS NOT NULL
                    ORDER BY observed_at DESC, received_at DESC LIMIT 1) p ON true
                  LEFT JOIN LATERAL (SELECT numeric_value, observed_at FROM ocpp_meter_samples WHERE transaction_id=t.id AND measurand='SoC'
                    AND numeric_value BETWEEN 0 AND 100 AND (unit IS NULL OR unit='Percent') AND phase IS NULL
                    AND format IS DISTINCT FROM 'SignedData' ORDER BY observed_at DESC, received_at DESC LIMIT 1) soc ON true
                  LEFT JOIN LATERAL (SELECT numeric_value, observed_at FROM ocpp_meter_samples WHERE transaction_id=t.id AND measurand='Temperature'
                    AND (unit IS NULL OR unit='Celsius') AND phase IS NULL AND format IS DISTINCT FROM 'SignedData'
                    ORDER BY observed_at DESC, received_at DESC LIMIT 1) temp ON true;
                UPDATE ocpp_charging_transactions SET session_id=id;
                UPDATE charging_connection_requests SET state='Expired' WHERE state='Waiting' AND expires_at <= CURRENT_TIMESTAMP;
                UPDATE charging_connection_requests r SET state='Completed' FROM charging_sessions s WHERE r.session_id=s.id AND s.ended_at IS NOT NULL;
                INSERT INTO charging_connection_requests (id, driver_id, station_id, connector_number, state, created_at, expires_at, linked_at, session_id)
                  SELECT t.id, t.driver_id, t.station_id, t.connector_number,
                    CASE WHEN t.stopped_at IS NULL THEN 'Linked' ELSE 'Completed' END,
                    t.start_received_at, t.start_received_at + interval '30 minutes', t.start_received_at, t.id
                  FROM ocpp_charging_transactions t WHERE t.driver_id IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM charging_connection_requests r WHERE r.session_id=t.id);
                DO $$ BEGIN
                  IF EXISTS (SELECT driver_id FROM charging_connection_requests WHERE state IN ('Waiting','Linked') GROUP BY driver_id HAVING count(*)>1)
                    OR EXISTS (SELECT station_id,connector_number FROM charging_connection_requests WHERE state IN ('Waiting','Linked') GROUP BY station_id,connector_number HAVING count(*)>1) THEN
                    RAISE EXCEPTION 'Conflicting legacy active claims: reconcile ownership before applying ChargingBusinessSessions. No owner has been chosen automatically.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "calculated_cost",
                table: "ocpp_charging_transactions");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "ocpp_charging_transactions");

            migrationBuilder.DropColumn(
                name: "current_power_kw",
                table: "ocpp_charging_transactions");

            migrationBuilder.DropColumn(
                name: "energy_kwh",
                table: "ocpp_charging_transactions");

            migrationBuilder.DropColumn(
                name: "last_measurement_at",
                table: "ocpp_charging_transactions");

            migrationBuilder.DropColumn(
                name: "unit_price_snapshot",
                table: "ocpp_charging_transactions");

            migrationBuilder.DropColumn(name: "driver_id", table: "ocpp_charging_transactions");

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_connectors_business_connector_id",
                table: "ocpp_connectors",
                column: "business_connector_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_charging_transactions_session_id",
                table: "ocpp_charging_transactions",
                column: "session_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_ocpp_transaction_meter",
                table: "ocpp_charging_transactions",
                sql: "meter_start_wh >= 0 AND (meter_stop_wh IS NULL OR meter_stop_wh >= 0)");

            migrationBuilder.CreateIndex(
                name: "IX_charging_connection_requests_session_id_station_id_connecto~",
                table: "charging_connection_requests",
                columns: new[] { "session_id", "station_id", "connector_number" });

            migrationBuilder.CreateIndex(
                name: "ux_claim_active_connector",
                table: "charging_connection_requests",
                columns: new[] { "station_id", "connector_number" },
                unique: true,
                filter: "state IN ('Waiting', 'Linked')");

            migrationBuilder.CreateIndex(
                name: "ux_claim_active_driver",
                table: "charging_connection_requests",
                column: "driver_id",
                unique: true,
                filter: "state IN ('Waiting', 'Linked')");

            migrationBuilder.CreateIndex(
                name: "IX_charging_connectors_station_id_number",
                table: "charging_connectors",
                columns: new[] { "station_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_charging_sessions_connector_id",
                table: "charging_sessions",
                column: "connector_id",
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_charging_sessions_connector_id_point_id_connector_number",
                table: "charging_sessions",
                columns: new[] { "connector_id", "point_id", "connector_number" });

            migrationBuilder.CreateIndex(
                name: "IX_charging_sessions_point_id_start_received_at",
                table: "charging_sessions",
                columns: new[] { "point_id", "start_received_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_charging_connection_requests_charging_sessions_session_id_s~",
                table: "charging_connection_requests",
                columns: new[] { "session_id", "station_id", "connector_number" },
                principalTable: "charging_sessions",
                principalColumns: new[] { "id", "point_id", "connector_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ocpp_charging_transactions_charging_sessions_session_id",
                table: "ocpp_charging_transactions",
                column: "session_id",
                principalTable: "charging_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ocpp_connectors_charging_connectors_business_connector_id",
                table: "ocpp_connectors",
                column: "business_connector_id",
                principalTable: "charging_connectors",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Downgrade requires an explicit data export and reconciliation plan; protocol-neutral sessions must not be discarded.");
        }
    }
}
