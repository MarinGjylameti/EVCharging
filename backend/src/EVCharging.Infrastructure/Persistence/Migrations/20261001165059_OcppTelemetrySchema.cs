using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OcppTelemetrySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "manual_restriction",
                table: "charging_stations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "status_observed_at",
                table: "charging_stations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "status_received_at",
                table: "charging_stations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status_source",
                table: "charging_stations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE charging_stations SET manual_restriction = status WHERE status IN ('OutOfService', 'Maintenance')"
            );

            migrationBuilder.CreateTable(
                name: "ocpp_charge_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    protocol_version = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    vendor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    charge_point_serial_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    charge_box_serial_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    firmware_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    iccid = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    imsi = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    meter_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    meter_serial_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_boot_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ocpp_charge_points", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "realtime_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_version = table.Column<long>(type: "bigint", nullable: false),
                    audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recipient_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_failure_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_realtime_outbox", x => x.id);
                    table.CheckConstraint("ck_realtime_outbox_audience", "audience IN ('PublicStation', 'Driver', 'Admin') AND (audience <> 'Driver' OR recipient_account_id IS NOT NULL)");
                    table.CheckConstraint("ck_realtime_outbox_version_attempts", "entity_version >= 0 AND attempts >= 0");
                    table.ForeignKey(
                        name: "FK_realtime_outbox_accounts_recipient_account_id",
                        column: x => x.recipient_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ocpp_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subprotocol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    disconnected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_frame_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_code = table.Column<int>(type: "integer", nullable: true),
                    close_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ocpp_connections", x => x.id);
                    table.CheckConstraint("ck_ocpp_connection_time", "disconnected_at IS NULL OR disconnected_at >= connected_at");
                    table.ForeignKey(
                        name: "FK_ocpp_connections_ocpp_charge_points_charge_point_id",
                        column: x => x.charge_point_id,
                        principalTable: "ocpp_charge_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ocpp_connectors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_number = table.Column<int>(type: "integer", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reported_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ocpp_connectors", x => x.id);
                    table.CheckConstraint("ck_ocpp_connector_number", "connector_number > 0");
                    table.ForeignKey(
                        name: "FK_ocpp_connectors_charging_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "charging_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ocpp_connectors_ocpp_charge_points_charge_point_id",
                        column: x => x.charge_point_id,
                        principalTable: "ocpp_charge_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ocpp_inbound_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    frame_json = table.Column<string>(type: "jsonb", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: true),
                    processing_state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ocpp_inbound_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_ocpp_inbound_messages_ocpp_charge_points_charge_point_id",
                        column: x => x.charge_point_id,
                        principalTable: "ocpp_charge_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ocpp_inbound_messages_ocpp_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "ocpp_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ocpp_charging_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_number = table.Column<int>(type: "integer", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ocpp16_transaction_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    start_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    start_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stopped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stop_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    physical_closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stop_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    meter_start_wh = table.Column<long>(type: "bigint", nullable: false),
                    meter_stop_wh = table.Column<long>(type: "bigint", nullable: true),
                    energy_kwh = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    current_power_kw = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    last_measurement_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    unit_price_snapshot = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    calculated_cost = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ocpp_charging_transactions", x => x.id);
                    table.CheckConstraint("ck_ocpp_transaction_meter", "meter_start_wh >= 0 AND (meter_stop_wh IS NULL OR meter_stop_wh >= 0) AND (energy_kwh IS NULL OR energy_kwh >= 0)");
                    table.CheckConstraint("ck_ocpp_transaction_price", "(unit_price_snapshot IS NULL OR unit_price_snapshot >= 0) AND (calculated_cost IS NULL OR calculated_cost >= 0)");
                    table.CheckConstraint("ck_ocpp_transaction_time", "stopped_at IS NULL OR stopped_at >= started_at");
                    table.ForeignKey(
                        name: "FK_ocpp_charging_transactions_accounts_driver_id",
                        column: x => x.driver_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ocpp_charging_transactions_charging_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "charging_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ocpp_charging_transactions_ocpp_charge_points_charge_point_~",
                        column: x => x.charge_point_id,
                        principalTable: "ocpp_charge_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ocpp_charging_transactions_ocpp_inbound_messages_start_mess~",
                        column: x => x.start_message_id,
                        principalTable: "ocpp_inbound_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ocpp_charging_transactions_ocpp_inbound_messages_stop_messa~",
                        column: x => x.stop_message_id,
                        principalTable: "ocpp_inbound_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ocpp_status_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    info = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    vendor_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    vendor_error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ocpp_status_events", x => x.id);
                    table.CheckConstraint("ck_ocpp_status_connector", "connector_number >= 0");
                    table.ForeignKey(
                        name: "FK_ocpp_status_events_ocpp_charge_points_charge_point_id",
                        column: x => x.charge_point_id,
                        principalTable: "ocpp_charge_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ocpp_status_events_ocpp_inbound_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "ocpp_inbound_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ocpp_meter_samples",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    connector_number = table.Column<int>(type: "integer", nullable: false),
                    meter_value_index = table.Column<int>(type: "integer", nullable: false),
                    sample_index = table.Column<int>(type: "integer", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    measurand = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    value_text = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    numeric_value = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    location = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    context = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ocpp_meter_samples", x => x.id);
                    table.CheckConstraint("ck_ocpp_sample_index", "connector_number >= 0 AND meter_value_index >= 0 AND sample_index >= 0");
                    table.ForeignKey(
                        name: "FK_ocpp_meter_samples_ocpp_charge_points_charge_point_id",
                        column: x => x.charge_point_id,
                        principalTable: "ocpp_charge_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ocpp_meter_samples_ocpp_charging_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "ocpp_charging_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ocpp_meter_samples_ocpp_inbound_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "ocpp_inbound_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_station_manual_restriction",
                table: "charging_stations",
                sql: "manual_restriction IS NULL OR manual_restriction IN ('OutOfService', 'Maintenance')");

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_charge_points_identity",
                table: "ocpp_charge_points",
                column: "identity",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_charge_points_last_seen_at",
                table: "ocpp_charge_points",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_charging_transactions_charge_point_id_connector_number",
                table: "ocpp_charging_transactions",
                columns: new[] { "charge_point_id", "connector_number" },
                unique: true,
                filter: "physical_closed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_charging_transactions_charge_point_id_external_transac~",
                table: "ocpp_charging_transactions",
                columns: new[] { "charge_point_id", "external_transaction_id" },
                unique: true,
                filter: "external_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_charging_transactions_driver_id_started_at",
                table: "ocpp_charging_transactions",
                columns: new[] { "driver_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_charging_transactions_ocpp16_transaction_id",
                table: "ocpp_charging_transactions",
                column: "ocpp16_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_charging_transactions_start_message_id",
                table: "ocpp_charging_transactions",
                column: "start_message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_charging_transactions_station_id_started_at",
                table: "ocpp_charging_transactions",
                columns: new[] { "station_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_charging_transactions_stop_message_id",
                table: "ocpp_charging_transactions",
                column: "stop_message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_connections_charge_point_id_connected_at",
                table: "ocpp_connections",
                columns: new[] { "charge_point_id", "connected_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_connectors_charge_point_id_connector_number",
                table: "ocpp_connectors",
                columns: new[] { "charge_point_id", "connector_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_connectors_station_id",
                table: "ocpp_connectors",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_inbound_messages_charge_point_id_received_at",
                table: "ocpp_inbound_messages",
                columns: new[] { "charge_point_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_inbound_messages_connection_id_call_id",
                table: "ocpp_inbound_messages",
                columns: new[] { "connection_id", "call_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_inbound_messages_processing_state_received_at",
                table: "ocpp_inbound_messages",
                columns: new[] { "processing_state", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_meter_samples_charge_point_id_connector_number_receive~",
                table: "ocpp_meter_samples",
                columns: new[] { "charge_point_id", "connector_number", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_meter_samples_message_id_meter_value_index_sample_index",
                table: "ocpp_meter_samples",
                columns: new[] { "message_id", "meter_value_index", "sample_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_meter_samples_transaction_id_observed_at",
                table: "ocpp_meter_samples",
                columns: new[] { "transaction_id", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_status_events_charge_point_id_connector_number_receive~",
                table: "ocpp_status_events",
                columns: new[] { "charge_point_id", "connector_number", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ocpp_status_events_message_id",
                table: "ocpp_status_events",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_realtime_outbox_entity_type_entity_id_entity_version",
                table: "realtime_outbox",
                columns: new[] { "entity_type", "entity_id", "entity_version" });

            migrationBuilder.CreateIndex(
                name: "IX_realtime_outbox_published_at_available_at",
                table: "realtime_outbox",
                columns: new[] { "published_at", "available_at" },
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_realtime_outbox_recipient_account_id_created_at",
                table: "realtime_outbox",
                columns: new[] { "recipient_account_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ocpp_connectors");

            migrationBuilder.DropTable(
                name: "ocpp_meter_samples");

            migrationBuilder.DropTable(
                name: "ocpp_status_events");

            migrationBuilder.DropTable(
                name: "realtime_outbox");

            migrationBuilder.DropTable(
                name: "ocpp_charging_transactions");

            migrationBuilder.DropTable(
                name: "ocpp_inbound_messages");

            migrationBuilder.DropTable(
                name: "ocpp_connections");

            migrationBuilder.DropTable(
                name: "ocpp_charge_points");

            migrationBuilder.DropCheckConstraint(
                name: "ck_station_manual_restriction",
                table: "charging_stations");

            migrationBuilder.DropColumn(
                name: "manual_restriction",
                table: "charging_stations");

            migrationBuilder.DropColumn(
                name: "status_observed_at",
                table: "charging_stations");

            migrationBuilder.DropColumn(
                name: "status_received_at",
                table: "charging_stations");

            migrationBuilder.DropColumn(
                name: "status_source",
                table: "charging_stations");
        }
    }
}
